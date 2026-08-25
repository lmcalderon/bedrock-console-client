namespace BedrockConsoleClient.Networking.RakNet;

using System.Net;

/// <summary>Public entry point for opening a RakNet connection.</summary>
public static class RakNetClient
{
  public static Task<RakNetConnection> ConnectAsync(
      IPEndPoint remoteEndPoint,
      RakNetConnectionOptions? options = null,
      Action<ConnectionState>? onStateChanged = null,
      CancellationToken ct = default)
      => RakNetConnection.ConnectAsync(remoteEndPoint, options, onStateChanged, ct);
}
