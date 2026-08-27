namespace BedrockConsoleClient.Networking.Bedrock;

using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using BedrockConsoleClient.Networking.Bedrock.Batch;
using BedrockConsoleClient.Networking.Bedrock.Encryption;
using BedrockConsoleClient.Networking.Bedrock.Identity;
using BedrockConsoleClient.Networking.Bedrock.Packets;
using BedrockConsoleClient.Networking.RakNet;

/// <summary>
/// Composes over a Connected RakNetConnection to drive the Bedrock
/// application-layer login sequence: network settings negotiation, the
/// self-signed offline-mode identity chain, the encryption handshake,
/// resource-pack negotiation, and StartGame through to spawn.
/// </summary>
public sealed class BedrockSession : IAsyncDisposable
{
  // Matches JwtSigner's encoder: the default one over-escapes safe ASCII
  // characters (e.g. base64 '+'/'/' inside Certificate/Token) as \uXXXX,
  // which this project's target servers don't decode correctly.
  private static readonly JsonSerializerOptions s_authInfoJsonOptions = new()
  {
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
  };

  private static readonly Dictionary<BedrockLoginState, BedrockLoginState[]> s_legalTransitions = new()
  {
    [BedrockLoginState.NotStarted] = [BedrockLoginState.AwaitingNetworkSettings],
    [BedrockLoginState.AwaitingNetworkSettings] = [BedrockLoginState.AwaitingPlayStatusLoginOk, BedrockLoginState.Disconnected],
    [BedrockLoginState.AwaitingPlayStatusLoginOk] = [BedrockLoginState.AwaitingResourcePacksInfo, BedrockLoginState.Disconnected],
    [BedrockLoginState.AwaitingResourcePacksInfo] = [BedrockLoginState.AwaitingResourcePackStack, BedrockLoginState.Disconnected],
    [BedrockLoginState.AwaitingResourcePackStack] = [BedrockLoginState.AwaitingStartGame, BedrockLoginState.Disconnected],
    [BedrockLoginState.AwaitingStartGame] = [BedrockLoginState.AwaitingItemRegistry, BedrockLoginState.Disconnected],
    [BedrockLoginState.AwaitingItemRegistry] = [BedrockLoginState.AwaitingSpawnConfirmation, BedrockLoginState.Disconnected],
    [BedrockLoginState.AwaitingSpawnConfirmation] = [BedrockLoginState.Spawned, BedrockLoginState.Disconnected],
    [BedrockLoginState.Spawned] = [BedrockLoginState.Disconnected],
    [BedrockLoginState.Disconnected] = [],
  };

  private readonly RakNetConnection _connection;
  private readonly BedrockLoginOptions _options;
  private readonly BedrockKeyPair _keyPair;
  private readonly IIdentityChainProvider _identityProvider;
  private readonly Lock _gate = new();
  private readonly TaskCompletionSource _spawnedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
  private readonly Action<string>? _onVerbose;

  private BedrockLoginState _state = BedrockLoginState.NotStarted;
  private CompressionAlgorithm? _negotiatedCompression;
  private int _compressionThreshold;
  private BedrockEncryptionContext? _encryption;
  private ulong _actorRuntimeId;

  // The two independent, order-unspecified conditions real BDS waits for
  // before considering the player spawned - see BedrockLoginState and
  // TryFinalizeSpawnAsync.
  private bool _chunkRadiusUpdated;
  private bool _playStatusPlayerSpawnReceived;

  public event Action<BedrockLoginState>? StateChanged;

  public BedrockLoginState State
  {
    get
    {
      lock (_gate)
      {
        return _state;
      }
    }
  }

  private BedrockSession(RakNetConnection connection, BedrockLoginOptions options, BedrockKeyPair keyPair, IIdentityChainProvider identityProvider, Action<string>? onVerbose)
  {
    _connection = connection;
    _options = options;
    _keyPair = keyPair;
    _identityProvider = identityProvider;
    _onVerbose = onVerbose;
  }

  public static async Task<BedrockSession> LoginAsync(
      RakNetConnection connection,
      BedrockLoginOptions options,
      IIdentityChainProvider identityProvider,
      Action<BedrockLoginState>? onStateChanged = null,
      Action<string>? onVerbose = null,
      CancellationToken ct = default)
  {
    var keyPair = BedrockKeyPair.Generate();
    var session = new BedrockSession(connection, options, keyPair, identityProvider, onVerbose);
    if (onStateChanged is not null)
    {
      session.StateChanged += onStateChanged;
    }

    connection.GamePacketReceived += session.OnRakNetGamePacketReceived;
    connection.StateChanged += session.OnRakNetStateChanged;

    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeoutCts.CancelAfter(options.LoginTimeout);

    session.TransitionTo(BedrockLoginState.AwaitingNetworkSettings);
    await session.SendGamePacketAsync(RequestNetworkSettings.Encode(options.ProtocolVersion), timeoutCts.Token);

    await session._spawnedTcs.Task.WaitAsync(timeoutCts.Token);
    return session;
  }

  // async void is the correct shape for an event handler (this project's own
  // async-conventions treat it as the one acceptable use); the try/catch
  // below ensures no exception can escape unhandled and crash the process.
#pragma warning disable VSTHRD100
  private async void OnRakNetGamePacketReceived(ReadOnlyMemory<byte> content)
#pragma warning restore VSTHRD100
  {
    try
    {
      List<byte[]> packets = DecodeBatch(content.Span);
      _onVerbose?.Invoke($"recv batch: {content.Length}b -> {packets.Count} packet(s)");
      foreach (byte[] packet in packets)
      {
        var (id, headerSize) = BedrockPacketHeader.ReadId(packet);
        _onVerbose?.Invoke($"recv packet id=0x{(uint)id:X2} ({id}) {packet.Length - headerSize}b");
        await HandlePacketAsync(packet, CancellationToken.None);
      }
    }
    catch (Exception ex)
    {
      _spawnedTcs.TrySetException(ex);
    }
  }

  private void OnRakNetStateChanged(ConnectionState state)
  {
    if (state != ConnectionState.Disconnected)
    {
      return;
    }

    lock (_gate)
    {
      if (_state == BedrockLoginState.Disconnected)
      {
        return;
      }
    }

    TransitionTo(BedrockLoginState.Disconnected);
    _spawnedTcs.TrySetException(new InvalidOperationException("RakNet connection was disconnected before Bedrock login completed."));
  }

  private List<byte[]> DecodeBatch(ReadOnlySpan<byte> frame)
  {
    if (frame.Length == 0 || frame[0] != 0xFE)
    {
      return [];
    }

    ReadOnlySpan<byte> body = frame[1..];
    BedrockEncryptionContext? encryption;
    lock (_gate)
    {
      encryption = _encryption;
    }

    byte[]? decrypted = null;
    if (encryption is not null)
    {
      decrypted = encryption.Decrypt(body);
      body = decrypted;
    }

    lock (_gate)
    {
      return _negotiatedCompression is not null
          ? PacketBatchCodec.Decode(body)
          : PacketBatchCodec.DecodeUnnegotiated(body);
    }
  }

  private async Task HandlePacketAsync(byte[] packet, CancellationToken ct)
  {
    var (id, headerSize) = BedrockPacketHeader.ReadId(packet);
    ReadOnlySpan<byte> payload = packet.AsSpan(headerSize);

    switch (id)
    {
      case BedrockPacketId.NetworkSettings:
        {
          var settings = NetworkSettings.Decode(payload);
          lock (_gate)
          {
            _negotiatedCompression = settings.CompressionAlgorithm;
            _compressionThreshold = settings.CompressionThreshold;
          }

          _onVerbose?.Invoke($"negotiated compression={settings.CompressionAlgorithm} threshold={settings.CompressionThreshold}");
          TransitionTo(BedrockLoginState.AwaitingPlayStatusLoginOk);
          await SendLoginAsync(ct);
          break;
        }

      case BedrockPacketId.ServerToClientHandshake:
        {
          string jwt = ServerToClientHandshake.DecodeJwt(payload);
          EnableEncryption(jwt);
          await SendGamePacketAsync(ClientToServerHandshake.Encode(), ct);
          break;
        }

      case BedrockPacketId.Disconnect:
        {
          var disconnect = Disconnect.Decode(payload);
          string reasonText = disconnect.Message is { Length: > 0 }
              ? disconnect.Message
              : $"reason code {disconnect.Reason}";
          throw new InvalidOperationException($"Server disconnected: {reasonText}");
        }

      case BedrockPacketId.PacketViolationWarning:
        {
          var violation = PacketViolationWarning.Decode(payload);
          throw new InvalidOperationException(
              $"Server reported a packet violation (type={violation.Type}, packetId=0x{violation.PacketId:X2}, " +
              $"severity={violation.Severity}): {violation.Message}");
        }

      case BedrockPacketId.PlayStatus:
        {
          var status = PlayStatus.Decode(payload);
          await HandlePlayStatusAsync(status, ct);
          break;
        }

      case BedrockPacketId.ResourcePacksInfo:
        {
          var info = ResourcePacksInfo.Decode(payload);
          if (info.PackCount != 0)
          {
            throw new NotSupportedException(
                $"Server requires {info.PackCount} resource pack(s); downloading packs is not supported by this client.");
          }

          TransitionTo(BedrockLoginState.AwaitingResourcePackStack);
          await SendGamePacketAsync(ResourcePackClientResponse.Encode(ResourcePackClientResponseStatus.HaveAllPacks), ct);
          break;
        }

      case BedrockPacketId.ResourcePackStack:
        TransitionTo(BedrockLoginState.AwaitingStartGame);
        await SendGamePacketAsync(ResourcePackClientResponse.Encode(ResourcePackClientResponseStatus.Completed), ct);
        break;

      case BedrockPacketId.StartGame:
        {
          _actorRuntimeId = StartGame.DecodeActorRuntimeId(payload);
          TransitionTo(BedrockLoginState.AwaitingItemRegistry);
          break;
        }

      case BedrockPacketId.ItemRegistry:
        TransitionTo(BedrockLoginState.AwaitingSpawnConfirmation);
        await SendGamePacketAsync(RequestChunkRadius.Encode(), ct);
        break;

      case BedrockPacketId.ChunkRadiusUpdated:
        _chunkRadiusUpdated = true;
        await TryFinalizeSpawnAsync(ct);
        break;

      default:
        // Anything else (world/entity/inventory packets, etc.) is out of
        // scope for this milestone. See PLAN.md.
        break;
    }
  }

  private async Task HandlePlayStatusAsync(PlayStatusCode status, CancellationToken ct)
  {
    if (status == PlayStatusCode.LoginSuccess)
    {
      TransitionTo(BedrockLoginState.AwaitingResourcePacksInfo);
      return;
    }

    // The second of the two independent conditions the real spawn sequence
    // waits for - see BedrockLoginState and TryFinalizeSpawnAsync.
    if (status == PlayStatusCode.PlayerSpawn)
    {
      _playStatusPlayerSpawnReceived = true;
      await TryFinalizeSpawnAsync(ct);
      return;
    }

    throw new InvalidOperationException($"Login failed: server returned PlayStatus {status}.");
  }

  // Mirrors gophertunnel's tryFinaliseClientConn: only once both
  // ChunkRadiusUpdated and PlayStatus(PlayerSpawn) have arrived (order
  // unspecified) does the client send SetLocalPlayerAsInitialized - see
  // BedrockLoginState for why this replaced sending it right after StartGame.
  private async Task TryFinalizeSpawnAsync(CancellationToken ct)
  {
    if (!_chunkRadiusUpdated || !_playStatusPlayerSpawnReceived)
    {
      return;
    }

    TransitionTo(BedrockLoginState.Spawned);
    await SendGamePacketAsync(SetLocalPlayerAsInitialized.Encode(_actorRuntimeId), ct);
    _spawnedTcs.TrySetResult();
  }

  private async Task SendLoginAsync(CancellationToken ct)
  {
    IdentityChainResult identity = await _identityProvider.ResolveAsync(_keyPair, ct);
    string clientDataJwt = ClientDataJwt.Build(_keyPair, _options.ServerAddress, _options.GameVersion, _options.Username);

    // Key order matters to real BDS's JSON parsing here (found by capturing
    // gophertunnel's own real, successful Login packet against this
    // project's BDS test container): Certificate must come first when
    // present, matching gophertunnel's own struct field order exactly - see
    // docs/notes/bedrock-login-design.md. Certificate is omitted entirely
    // (not even a placeholder) when the provider has none - see
    // IdentityChainResult.Certificate.
    var authInfo = new Dictionary<string, object>();
    if (identity.Certificate is not null)
    {
      authInfo["Certificate"] = identity.Certificate;
    }

    authInfo["AuthenticationType"] = identity.AuthenticationType;
    authInfo["Token"] = identity.Token;

    string authInfoJson = JsonSerializer.Serialize(authInfo, s_authInfoJsonOptions);

    await SendGamePacketAsync(Login.Encode(_options.ProtocolVersion, authInfoJson, clientDataJwt), ct);
  }

  private void EnableEncryption(string serverToClientHandshakeJwt)
  {
    var (header, payload, signature, signingInput) = JwtSigner.Decode(serverToClientHandshakeJwt);
    string serverX5u = header.GetProperty("x5u").GetString()
        ?? throw new InvalidOperationException("ServerToClientHandshake JWT is missing x5u.");

    using ECDsa serverVerifyKey = JwtSigner.ImportPublicKeyFromDerBase64(serverX5u);
    if (!JwtSigner.Verify(signingInput, signature, serverVerifyKey))
    {
      throw new InvalidOperationException("ServerToClientHandshake JWT signature is invalid.");
    }

    string saltBase64 = payload.GetProperty("salt").GetString()
        ?? throw new InvalidOperationException("ServerToClientHandshake JWT is missing salt.");
    byte[] salt = Convert.FromBase64String(saltBase64);

    using ECDiffieHellman clientEcdh = _keyPair.CreateDiffieHellman();
    using var serverEcdh = ECDiffieHellman.Create();
    serverEcdh.ImportSubjectPublicKeyInfo(Convert.FromBase64String(serverX5u), out _);

    byte[] key = HandshakeKeyExchange.DeriveKey(clientEcdh, serverEcdh, salt);
    lock (_gate)
    {
      _encryption = new BedrockEncryptionContext(key);
    }
  }

  private Task SendGamePacketAsync(byte[] packet, CancellationToken ct) => SendBatchAsync([packet], ct);

  private async Task SendBatchAsync(IReadOnlyList<byte[]> packets, CancellationToken ct)
  {
    if (_onVerbose is not null)
    {
      foreach (byte[] packet in packets)
      {
        var (id, headerSize) = BedrockPacketHeader.ReadId(packet);
        _onVerbose($"send packet id=0x{(uint)id:X2} ({id}) {packet.Length - headerSize}b");
      }
    }

    CompressionAlgorithm? compression;
    int threshold;
    BedrockEncryptionContext? encryption;
    lock (_gate)
    {
      compression = _negotiatedCompression;
      threshold = _compressionThreshold;
      encryption = _encryption;
    }

    byte[] body = compression is not null
        ? PacketBatchCodec.Encode(packets, compression.Value, threshold)
        : PacketBatchCodec.EncodeUnnegotiated(packets);

    if (encryption is not null)
    {
      body = encryption.Encrypt(body);
    }

    var frame = new byte[1 + body.Length];
    frame[0] = 0xFE;
    body.CopyTo(frame.AsSpan(1));

    await _connection.SendGamePacketAsync(frame, ct);
  }

  private void TransitionTo(BedrockLoginState next)
  {
    lock (_gate)
    {
      if (!s_legalTransitions.TryGetValue(_state, out var allowed) || !allowed.Contains(next))
      {
        throw new InvalidOperationException($"Illegal Bedrock login state transition: {_state} -> {next}");
      }

      _state = next;
    }

    StateChanged?.Invoke(next);
  }

  public async ValueTask DisposeAsync()
  {
    _connection.GamePacketReceived -= OnRakNetGamePacketReceived;
    _connection.StateChanged -= OnRakNetStateChanged;
    _encryption?.Dispose();
    _keyPair.Dispose();
    await _connection.DisconnectAsync();
  }
}
