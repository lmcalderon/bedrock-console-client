namespace BedrockConsoleClient.Networking.RakNet;

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using BedrockConsoleClient.Networking.RakNet.IO;
using BedrockConsoleClient.Networking.RakNet.Packets;
using BedrockConsoleClient.Networking.RakNet.Reliability;

/// <summary>
/// A live (or in-progress) RakNet session: connection lifecycle state, the
/// offline/connected handshake, the reliability layer (datagrams, frames,
/// ACK/NAK, resend), and the ConnectedPing/Pong keep-alive loop.
/// </summary>
public sealed class RakNetConnection : IAsyncDisposable
{
  // Full network-MTU candidates to probe, largest first. 1492 = common
  // non-fragmenting Ethernet MTU minus overhead; 1200 and 576 are conservative
  // fallbacks for lossy/tunnelled paths.
  private static readonly int[] s_mtuCandidates = [1492, 1200, 576];

  private static readonly Dictionary<ConnectionState, ConnectionState[]> s_legalTransitions = new()
  {
    [ConnectionState.Unconnected] = [ConnectionState.OfflineHandshake1],
    [ConnectionState.OfflineHandshake1] = [ConnectionState.OfflineHandshake2, ConnectionState.Disconnected],
    [ConnectionState.OfflineHandshake2] = [ConnectionState.ConnectedHandshake, ConnectionState.Disconnected],
    [ConnectionState.ConnectedHandshake] = [ConnectionState.Connected, ConnectionState.Disconnected],
    [ConnectionState.Connected] = [ConnectionState.Disconnected],
    [ConnectionState.Disconnected] = [],
  };

  private readonly UdpClient _socket;
  private readonly IPEndPoint _remoteEndPoint;
  private readonly RakNetConnectionOptions _options;
  private readonly long _clientGuid;
  private readonly Lock _gate = new();
  private readonly Dictionary<uint, ResendEntry> _resendCache = [];
  private readonly SplitPacketReassembler _splitReassembler = new();
  private readonly TaskCompletionSource _connectedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

  private ConnectionState _state = ConnectionState.Unconnected;
  private bool _serverHasSecurity;
  private uint _cookie;
  private ushort _mtu;
  private uint _nextDatagramSequenceNumber;
  private uint _nextMessageIndex;
  private uint _nextOrderIndex;
  private ushort _nextSplitId;
  private long _lastPingSentTimestampMs;

  private CancellationTokenSource? _lifetimeCts;
  private Task? _receiveLoopTask;
  private Task? _maintenanceLoopTask;

  public event Action<ConnectionState>? StateChanged;

  public event Action<TimeSpan>? PingRoundTripMeasured;

  // Fired when the connection is torn down because a reliable datagram was
  // never acknowledged after MaxResendAttempts. Distinct from a clean,
  // server-initiated DisconnectNotification.
  public event Action<string>? ConnectionLost;

  // Fired for any frame payload this layer doesn't recognize as a RakNet
  // message: in practice, a Bedrock game packet batch once Connected.
  public event Action<ReadOnlyMemory<byte>>? GamePacketReceived;

  public ConnectionState State
  {
    get
    {
      lock (_gate)
      {
        return _state;
      }
    }
  }

  private RakNetConnection(UdpClient socket, IPEndPoint remoteEndPoint, RakNetConnectionOptions options)
  {
    _socket = socket;
    _remoteEndPoint = remoteEndPoint;
    _options = options;
    _clientGuid = -Random.Shared.NextInt64(1, long.MaxValue);
  }

  public static async Task<RakNetConnection> ConnectAsync(
      IPEndPoint remoteEndPoint,
      RakNetConnectionOptions? options = null,
      Action<ConnectionState>? onStateChanged = null,
      CancellationToken ct = default)
  {
    options ??= new RakNetConnectionOptions();
    var socket = new UdpClient(remoteEndPoint.AddressFamily);
    socket.Connect(remoteEndPoint);
    var connection = new RakNetConnection(socket, remoteEndPoint, options);
    if (onStateChanged is not null)
    {
      connection.StateChanged += onStateChanged;
    }

    using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    handshakeCts.CancelAfter(options.HandshakeOverallTimeout);
    try
    {
      await connection.PerformOfflineHandshakeAsync(handshakeCts.Token);

      connection._lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      connection._receiveLoopTask = connection.ReceiveLoopAsync(connection._lifetimeCts.Token);
      connection._maintenanceLoopTask = connection.MaintenanceLoopAsync(connection._lifetimeCts.Token);

      await connection.PerformConnectedHandshakeAsync(handshakeCts.Token);
    }
    catch
    {
      if (connection._lifetimeCts is not null)
      {
        await connection._lifetimeCts.CancelAsync();
      }

      socket.Dispose();
      throw;
    }

    return connection;
  }

  private async Task PerformOfflineHandshakeAsync(CancellationToken ct)
  {
    TransitionTo(ConnectionState.OfflineHandshake1);
    byte[] reply1Bytes = await ProbeMtuAsync(ct);
    var reply1 = OpenConnectionReply1.Decode(reply1Bytes.AsSpan(1));
    _serverHasSecurity = reply1.ServerHasSecurity;
    _cookie = reply1.Cookie;
    _mtu = reply1.Mtu;

    TransitionTo(ConnectionState.OfflineHandshake2);
    byte[] reply2Bytes = await HandshakeStepAsync(
        sendAsync: sendCt => SendRawAsync(
            OpenConnectionRequest2.Encode(_remoteEndPoint, _mtu, _clientGuid, _serverHasSecurity, _cookie), sendCt),
        expectedReplyId: RakNetMessageId.OpenConnectionReply2,
        ct);
    var reply2 = OpenConnectionReply2.Decode(reply2Bytes.AsSpan(1));
    _mtu = reply2.Mtu;
  }

  private async Task<byte[]> ProbeMtuAsync(CancellationToken ct)
  {
    foreach (int mtuCandidate in s_mtuCandidates)
    {
      using var candidateCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      candidateCts.CancelAfter(_options.HandshakeStepTimeout * 4);
      try
      {
        return await HandshakeStepAsync(
            sendAsync: sendCt => SendRawAsync(OpenConnectionRequest1.Encode(_options.ProtocolVersion, mtuCandidate), sendCt),
            expectedReplyId: RakNetMessageId.OpenConnectionReply1,
            candidateCts.Token);
      }
      catch (OperationCanceledException) when (!ct.IsCancellationRequested)
      {
        // This MTU candidate got no reply within its budget; fall through to
        // the next smaller one rather than failing the whole handshake.
      }
    }

    throw new TimeoutException("No OpenConnectionReply1 received for any MTU candidate.");
  }

  private async Task<byte[]> HandshakeStepAsync(
      Func<CancellationToken, Task> sendAsync, RakNetMessageId expectedReplyId, CancellationToken ct)
  {
    using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    var senderTask = ResendUntilCancelledAsync(sendAsync, _options.HandshakeStepTimeout, stepCts.Token);
    try
    {
      return await ReceiveOfflineMessageAsync(expectedReplyId, ct);
    }
    finally
    {
      await stepCts.CancelAsync();
      try
      {
        await senderTask;
      }
      catch (OperationCanceledException)
      {
        // Expected: the sender loop only stops via stepCts cancellation.
      }
    }
  }

  private static async Task ResendUntilCancelledAsync(Func<CancellationToken, Task> sendAsync, TimeSpan interval, CancellationToken ct)
  {
    while (true)
    {
      await sendAsync(ct);
      await Task.Delay(interval, ct);
    }
  }

  private async Task<byte[]> ReceiveOfflineMessageAsync(RakNetMessageId expectedId, CancellationToken ct)
  {
    while (true)
    {
      var result = await _socket.ReceiveAsync(ct);
      if (result.Buffer.Length > 0 && result.Buffer[0] == (byte)expectedId)
      {
        return result.Buffer;
      }

      // Ignore anything else (stray retransmits, unrelated offline IDs) and keep waiting.
    }
  }

  private async Task SendRawAsync(byte[] payload, CancellationToken ct) => await _socket.SendAsync(payload, ct);

  private async Task PerformConnectedHandshakeAsync(CancellationToken ct)
  {
    TransitionTo(ConnectionState.ConnectedHandshake);
    long requestTime = Environment.TickCount64;
    await SendFrameAsync(ConnectionRequest.Encode(_clientGuid, requestTime), FrameReliability.ReliableOrdered, ct);
    await _connectedTcs.Task.WaitAsync(ct);
  }

  private async Task ReceiveLoopAsync(CancellationToken ct)
  {
    try
    {
      while (!ct.IsCancellationRequested)
      {
        var result = await _socket.ReceiveAsync(ct);
        await ProcessIncomingAsync(result.Buffer, ct);
      }
    }
    catch (OperationCanceledException)
    {
    }
    catch (ObjectDisposedException)
    {
    }
  }

  private async Task ProcessIncomingAsync(byte[] data, CancellationToken ct)
  {
    if (data.Length == 0)
    {
      return;
    }

    byte header = data[0];

    // ACK datagram.
    if ((header & 0x40) != 0)
    {
      var acked = AckNakCodec.Read(data.AsSpan(1));
      lock (_gate)
      {
        foreach (uint seq in acked)
        {
          _resendCache.Remove(seq);
        }
      }

      return;
    }

    // NAK datagram.
    if ((header & 0x20) != 0)
    {
      var lost = AckNakCodec.Read(data.AsSpan(1));

      // NAK is an explicit, reliable "resend this" signal from the peer.
      // Honor it immediately and don't count it against the backoff/give-up
      // budget, unlike a plain timeout-based resend.
      await ResendDatagramsAsync(lost, incrementAttempt: false, ct);
      return;
    }

    if ((header & Datagram.DatagramFlag) == 0)
    {
      return; // Not a datagram we understand at this layer; ignore.
    }

    var datagram = Datagram.Read(data);
    await SendAckAsync(datagram.SequenceNumber, ct);
    foreach (var frame in datagram.Frames)
    {
      byte[]? content = frame.IsSplit ? _splitReassembler.Add(frame) : frame.Content.ToArray();
      if (content is null)
      {
        continue; // split message still waiting on more fragments
      }

      if (frame.Reliability.IsSequencedOrOrdered())
      {
        foreach (byte[] ordered in ReleaseInOrder(frame.OrderIndex, content))
        {
          await HandleConnectedPacketAsync(ordered, ct);
        }
      }
      else
      {
        await HandleConnectedPacketAsync(content, ct);
      }
    }
  }

  // UDP doesn't guarantee delivery order, but this project's Bedrock
  // application layer depends on strictly gapless, in-order batches (its
  // encryption counters desync otherwise). This client only ever uses order
  // channel 0, so a single expected-index counter plus a buffer for
  // out-of-order arrivals is enough; no need to track multiple channels.
  private readonly SortedDictionary<uint, byte[]> _reorderBuffer = [];
  private uint _nextExpectedOrderIndex;

  private List<byte[]> ReleaseInOrder(uint orderIndex, byte[] content)
  {
    var released = new List<byte[]>();
    if (orderIndex < _nextExpectedOrderIndex)
    {
      return released; // already delivered - duplicate, drop it
    }

    _reorderBuffer[orderIndex] = content;
    while (_reorderBuffer.TryGetValue(_nextExpectedOrderIndex, out byte[]? next))
    {
      released.Add(next);
      _reorderBuffer.Remove(_nextExpectedOrderIndex);
      _nextExpectedOrderIndex++;
    }

    return released;
  }

  private async Task HandleConnectedPacketAsync(ReadOnlyMemory<byte> content, CancellationToken ct)
  {
    if (content.Length == 0)
    {
      return;
    }

    var id = (RakNetMessageId)content.Span[0];
    switch (id)
    {
      case RakNetMessageId.ConnectionRequestAccepted:
        {
          var accepted = ConnectionRequestAccepted.Decode(content.Span[1..]);
          byte[] nic = NewIncomingConnection.Encode(_remoteEndPoint, accepted.PongTime, Environment.TickCount64);
          await SendFrameAsync(nic, FrameReliability.ReliableOrdered, ct);
          TransitionTo(ConnectionState.Connected);
          _connectedTcs.TrySetResult();
          break;
        }

      case RakNetMessageId.ConnectedPing:
        {
          long pingTime = BinaryPrimitives.ReadInt64BigEndian(content.Span[1..]);
          await SendFrameAsync(ConnectedPong.Encode(pingTime, Environment.TickCount64), FrameReliability.Unreliable, ct);
          break;
        }

      case RakNetMessageId.ConnectedPong:
        {
          var pong = ConnectedPong.Decode(content.Span[1..]);
          PingRoundTripMeasured?.Invoke(TimeSpan.FromMilliseconds(Environment.TickCount64 - pong.PingTime));
          break;
        }

      case RakNetMessageId.DisconnectNotification:
        TransitionTo(ConnectionState.Disconnected);
        if (_lifetimeCts is not null)
        {
          await _lifetimeCts.CancelAsync();
        }

        break;
      default:
        // Unrecognized at the RakNet layer. Handed off as-is rather than
        // parsed here, keeping RakNet itself protocol-agnostic. The Bedrock
        // application layer (Networking/Bedrock/) subscribes to this event.
        GamePacketReceived?.Invoke(content);
        break;
    }
  }

  private async Task MaintenanceLoopAsync(CancellationToken ct)
  {
    using var timer = new PeriodicTimer(_options.ResendInterval);
    try
    {
      while (await timer.WaitForNextTickAsync(ct))
      {
        await ResendOverdueDatagramsAsync(ct);
        await MaybeSendKeepAlivePingAsync(ct);
      }
    }
    catch (OperationCanceledException)
    {
    }
  }

  // Exponential backoff: ResendInterval, x2, x4, ... capped at MaxResendInterval.
  private TimeSpan BackoffDelay(int attemptCount)
  {
    double multiplier = Math.Pow(2, attemptCount);
    double delayMs = Math.Min(_options.ResendInterval.TotalMilliseconds * multiplier, _options.MaxResendInterval.TotalMilliseconds);
    return TimeSpan.FromMilliseconds(delayMs);
  }

  private async Task ResendOverdueDatagramsAsync(CancellationToken ct)
  {
    List<uint>? toResend = null;
    List<uint>? toGiveUp = null;
    var now = DateTime.UtcNow;
    lock (_gate)
    {
      foreach (var (seq, entry) in _resendCache)
      {
        if (now < entry.SentAtUtc + BackoffDelay(entry.AttemptCount))
        {
          continue;
        }

        if (entry.AttemptCount >= _options.MaxResendAttempts)
        {
          (toGiveUp ??= []).Add(seq);
        }
        else
        {
          (toResend ??= []).Add(seq);
        }
      }

      if (toGiveUp is not null)
      {
        foreach (uint seq in toGiveUp)
        {
          _resendCache.Remove(seq);
        }
      }
    }

    if (toGiveUp is not null)
    {
      ConnectionLost?.Invoke($"Gave up after {_options.MaxResendAttempts} unacknowledged resends of a reliable datagram.");
      if (State != ConnectionState.Disconnected)
      {
        TransitionTo(ConnectionState.Disconnected);
        if (_lifetimeCts is not null)
        {
          await _lifetimeCts.CancelAsync();
        }
      }
    }

    if (toResend is not null)
    {
      await ResendDatagramsAsync(toResend, incrementAttempt: true, ct);
    }
  }

  private async Task ResendDatagramsAsync(IReadOnlyList<uint> sequenceNumbers, bool incrementAttempt, CancellationToken ct)
  {
    foreach (uint seq in sequenceNumbers)
    {
      ResendEntry entry;
      bool found;
      lock (_gate)
      {
        found = _resendCache.TryGetValue(seq, out entry);
      }

      if (!found)
      {
        continue;
      }

      await _socket.SendAsync(entry.Bytes.AsMemory(0, entry.Length), ct);
      lock (_gate)
      {
        if (_resendCache.ContainsKey(seq))
        {
          _resendCache[seq] = entry with
          {
            SentAtUtc = DateTime.UtcNow,
            AttemptCount = incrementAttempt ? entry.AttemptCount + 1 : entry.AttemptCount,
          };
        }
      }
    }
  }

  private async Task MaybeSendKeepAlivePingAsync(CancellationToken ct)
  {
    long now = Environment.TickCount64;
    bool due;
    lock (_gate)
    {
      due = now - _lastPingSentTimestampMs >= _options.KeepAliveInterval.TotalMilliseconds;
    }

    if (!due)
    {
      return;
    }

    lock (_gate)
    {
      _lastPingSentTimestampMs = now;
    }

    await SendFrameAsync(ConnectedPing.Encode(now), FrameReliability.Unreliable, ct);
  }

  // Content too large for one MTU-sized datagram is split across multiple
  // Frames sharing one SplitId. Each fragment gets its own MessageIndex (each
  // is ACKed/resent independently), but all fragments of one logical message
  // share a single OrderIndex, assigned once: order applies to the
  // reassembled message, not to the individual wire fragments.
  private async Task SendFrameAsync(byte[] payload, FrameReliability reliability, CancellationToken ct)
  {
    int maxSingleFrame = MaxFrameContentSize(reliability, split: false);
    if (payload.Length <= maxSingleFrame)
    {
      await SendOneFrameAsync(payload, reliability, splitInfo: null, sharedOrderIndex: null, ct);
      return;
    }

    int maxFragment = MaxFrameContentSize(reliability, split: true);
    int fragmentCount = (payload.Length + maxFragment - 1) / maxFragment;
    ushort splitId;
    uint? sharedOrderIndex = null;
    lock (_gate)
    {
      splitId = _nextSplitId++;
      if (reliability.IsSequencedOrOrdered())
      {
        sharedOrderIndex = _nextOrderIndex++;
      }
    }

    for (int i = 0; i < fragmentCount; i++)
    {
      int offset = i * maxFragment;
      int length = Math.Min(maxFragment, payload.Length - offset);
      byte[] fragment = payload.AsSpan(offset, length).ToArray();
      var splitInfo = ((uint)fragmentCount, splitId, (uint)i);
      await SendOneFrameAsync(fragment, reliability, splitInfo, sharedOrderIndex, ct);
    }
  }

  private int MaxFrameContentSize(FrameReliability reliability, bool split)
  {
    int frameHeader = 3
        + (reliability.IsReliable() ? 3 : 0)
        + (reliability.IsSequenced() ? 3 : 0)
        + (reliability.IsSequencedOrOrdered() ? 4 : 0)
        + (split ? 10 : 0);
    const int datagramHeader = 4; // 1 header byte + 3-byte sequence number
    return _mtu - datagramHeader - frameHeader;
  }

  private async Task SendOneFrameAsync(
      byte[] content,
      FrameReliability reliability,
      (uint Count, ushort Id, uint Index)? splitInfo,
      uint? sharedOrderIndex,
      CancellationToken ct)
  {
    uint messageIndex = 0, orderIndex = 0, sequenceNumber;
    lock (_gate)
    {
      if (reliability.IsReliable())
      {
        messageIndex = _nextMessageIndex++;
      }

      if (reliability.IsSequencedOrOrdered())
      {
        orderIndex = sharedOrderIndex ?? _nextOrderIndex++;
      }

      sequenceNumber = _nextDatagramSequenceNumber++;
    }

    var frame = new Frame
    {
      Reliability = reliability,
      MessageIndex = messageIndex,
      OrderIndex = orderIndex,
      IsSplit = splitInfo is not null,
      SplitCount = splitInfo?.Count ?? 0,
      SplitId = splitInfo?.Id ?? 0,
      SplitIndex = splitInfo?.Index ?? 0,
      Content = content,
    };
    var datagram = new Datagram { SequenceNumber = sequenceNumber, Frames = [frame] };
    var buffer = new byte[_mtu];
    int written = datagram.Write(buffer);

    if (reliability.IsReliable())
    {
      lock (_gate)
      {
        _resendCache[sequenceNumber] = new ResendEntry(buffer, written, DateTime.UtcNow);
      }
    }

    await _socket.SendAsync(buffer.AsMemory(0, written), ct);
  }

  private async Task SendAckAsync(uint sequenceNumber, CancellationToken ct)
  {
    var buffer = new byte[16];
    buffer[0] = Datagram.DatagramFlag | 0x40;
    int written = 1 + AckNakCodec.Write(buffer.AsSpan(1), [sequenceNumber]);
    await _socket.SendAsync(buffer.AsMemory(0, written), ct);
  }

  private void TransitionTo(ConnectionState next)
  {
    lock (_gate)
    {
      if (!s_legalTransitions.TryGetValue(_state, out var allowed) || !allowed.Contains(next))
      {
        throw new InvalidOperationException($"Illegal RakNet state transition: {_state} -> {next}");
      }

      _state = next;
    }

    StateChanged?.Invoke(next);
  }

  // Public entry point for higher application layers (e.g. Networking/Bedrock)
  // to send arbitrary payloads once Connected. Always reliable-ordered: the
  // one reliability level every Bedrock handshake/game packet needs here.
  public Task SendGamePacketAsync(byte[] payload, CancellationToken ct = default) =>
      SendFrameAsync(payload, FrameReliability.ReliableOrdered, ct);

  public async Task DisconnectAsync(CancellationToken ct = default)
  {
    if (State == ConnectionState.Disconnected)
    {
      return;
    }

    try
    {
      await SendFrameAsync([(byte)RakNetMessageId.DisconnectNotification], FrameReliability.Reliable, ct);
    }
    catch
    {
      // Best-effort notification; the peer will time out either way.
    }

    TransitionTo(ConnectionState.Disconnected);
    if (_lifetimeCts is not null)
    {
      await _lifetimeCts.CancelAsync();
    }

    // These join background loops this same connection started earlier in
    // ConnectAsync: an intentional "wind down on dispose" pattern, not the
    // cross-context task hazard VSTHRD003 usually guards against. That rule
    // targets UI-thread JoinableTaskContext deadlocks, which don't apply to
    // this plain console app.
#pragma warning disable VSTHRD003
    await WaitBrieflyAsync(_receiveLoopTask);
    await WaitBrieflyAsync(_maintenanceLoopTask);
#pragma warning restore VSTHRD003
    _socket.Dispose();
  }

  private static async Task WaitBrieflyAsync(Task? task)
  {
    if (task is null)
    {
      return;
    }

    try
    {
      await task.WaitAsync(TimeSpan.FromSeconds(1));
    }
    catch (Exception) when (task.IsCompleted)
    {
      // Loop task already ended (canceled or otherwise); nothing more to wait for.
    }
    catch (TimeoutException)
    {
    }
  }

  public async ValueTask DisposeAsync()
  {
    await DisconnectAsync();
    _lifetimeCts?.Dispose();
  }

  private readonly record struct ResendEntry(byte[] Bytes, int Length, DateTime SentAtUtc, int AttemptCount = 0);
}
