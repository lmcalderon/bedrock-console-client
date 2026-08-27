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

  /// <summary>
  /// Reads a server's advertised MOTD (protocol version, game version, ...)
  /// via a standalone unconnected ping - no connection is opened.
  /// </summary>
  public static Task<RakNetServerInfo> QueryServerAsync(
      IPEndPoint remoteEndPoint, TimeSpan? timeout = null, CancellationToken ct = default)
      => RakNetServerQuery.QueryAsync(remoteEndPoint, timeout ?? TimeSpan.FromSeconds(3), ct);
}
