namespace BedrockConsoleClient.Networking.Bedrock;

using System.Security.Cryptography;
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
  private static readonly Dictionary<BedrockLoginState, BedrockLoginState[]> s_legalTransitions = new()
  {
    [BedrockLoginState.NotStarted] = [BedrockLoginState.AwaitingNetworkSettings],
    [BedrockLoginState.AwaitingNetworkSettings] = [BedrockLoginState.AwaitingPlayStatusLoginOk, BedrockLoginState.Disconnected],
    [BedrockLoginState.AwaitingPlayStatusLoginOk] = [BedrockLoginState.AwaitingResourcePacksInfo, BedrockLoginState.Disconnected],
    [BedrockLoginState.AwaitingResourcePacksInfo] = [BedrockLoginState.AwaitingResourcePackStack, BedrockLoginState.Disconnected],
    [BedrockLoginState.AwaitingResourcePackStack] = [BedrockLoginState.AwaitingStartGame, BedrockLoginState.Disconnected],
    [BedrockLoginState.AwaitingStartGame] = [BedrockLoginState.Spawned, BedrockLoginState.Disconnected],
    [BedrockLoginState.Spawned] = [BedrockLoginState.Disconnected],
    [BedrockLoginState.Disconnected] = [],
  };

  private readonly RakNetConnection _connection;
  private readonly BedrockLoginOptions _options;
  private readonly BedrockKeyPair _keyPair;
  private readonly IIdentityChainProvider _identityProvider;
  private readonly Lock _gate = new();
  private readonly TaskCompletionSource _spawnedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

  private BedrockLoginState _state = BedrockLoginState.NotStarted;
  private CompressionAlgorithm? _negotiatedCompression;
  private int _compressionThreshold;
  private BedrockEncryptionContext? _encryption;
  private ulong _actorRuntimeId;

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

  private BedrockSession(RakNetConnection connection, BedrockLoginOptions options, BedrockKeyPair keyPair, IIdentityChainProvider identityProvider)
  {
    _connection = connection;
    _options = options;
    _keyPair = keyPair;
    _identityProvider = identityProvider;
  }

  public static async Task<BedrockSession> LoginAsync(
      RakNetConnection connection,
      BedrockLoginOptions options,
      IIdentityChainProvider identityProvider,
      Action<BedrockLoginState>? onStateChanged = null,
      CancellationToken ct = default)
  {
    var keyPair = BedrockKeyPair.Generate();
    var session = new BedrockSession(connection, options, keyPair, identityProvider);
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
      foreach (byte[] packet in packets)
      {
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

          // PMMP's SpawnResponsePacketHandler waits for this immediately after
          // StartGame; there is no separate PlayStatus(PLAYER_SPAWN) to wait
          // for first (confirmed after an earlier attempt stalled here).
          TransitionTo(BedrockLoginState.Spawned);
          await SendGamePacketAsync(SetLocalPlayerAsInitialized.Encode(_actorRuntimeId), ct);
          _spawnedTcs.TrySetResult();
          break;
        }

      default:
        // Anything else (world/entity/inventory packets, etc.) is out of
        // scope for this milestone. See PLAN.md.
        break;
    }
  }

  private Task HandlePlayStatusAsync(PlayStatusCode status, CancellationToken ct)
  {
    if (status == PlayStatusCode.LoginSuccess)
    {
      TransitionTo(BedrockLoginState.AwaitingResourcePacksInfo);
      return Task.CompletedTask;
    }

    // PlayerSpawn isn't a trigger this client waits for (see the StartGame
    // case), but tolerate it arriving anyway rather than treating it as a
    // failure. Some servers or protocol variants may still send it.
    if (status == PlayStatusCode.PlayerSpawn)
    {
      return Task.CompletedTask;
    }

    throw new InvalidOperationException($"Login failed: server returned PlayStatus {status}.");
  }

  private async Task SendLoginAsync(CancellationToken ct)
  {
    IdentityChainResult identity = await _identityProvider.ResolveAsync(_keyPair, ct);
    string clientDataJwt = ClientDataJwt.Build(_keyPair, _options.ServerAddress);

    // Certificate is the real Bedrock wire format's chain-of-trust; PMMP's
    // current AuthenticationInfo model happens not to read it, but other
    // servers may, so it's still sent whenever the provider has one.
    string authInfoJson = identity.Certificate is null
        ? JsonSerializer.Serialize(new { identity.AuthenticationType, identity.Token })
        : JsonSerializer.Serialize(new { identity.AuthenticationType, identity.Certificate, identity.Token });

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
