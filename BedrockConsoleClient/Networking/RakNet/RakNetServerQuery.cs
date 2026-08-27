namespace BedrockConsoleClient.Networking.RakNet;

using System.Net;
using System.Net.Sockets;
using BedrockConsoleClient.Networking.RakNet.Packets;

// A standalone unconnected ping/pong exchange: reads a server's advertised
// MOTD (protocol version, game version, player counts, ...) without
// performing a full handshake/connection. Lets callers surface a protocol
// mismatch up front instead of only discovering it via an opaque
// PlayStatus.LoginFailedClient/LoginFailedServer response mid-login.
internal static class RakNetServerQuery
{
  public static async Task<RakNetServerInfo> QueryAsync(
      IPEndPoint remoteEndPoint, TimeSpan timeout, CancellationToken ct = default)
  {
    using var socket = new UdpClient(remoteEndPoint.AddressFamily);
    socket.Connect(remoteEndPoint);

    long clientGuid = -Random.Shared.NextInt64(1, long.MaxValue);
    byte[] ping = UnconnectedPing.Encode(clientGuid);

    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeoutCts.CancelAfter(timeout);

    using var senderCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);
    Task senderTask = ResendUntilCancelledAsync(socket, ping, TimeSpan.FromMilliseconds(500), senderCts.Token);
    try
    {
      while (true)
      {
        var result = await socket.ReceiveAsync(timeoutCts.Token);
        if (result.Buffer.Length > 0 && result.Buffer[0] == (byte)RakNetMessageId.UnconnectedPong)
        {
          var pong = UnconnectedPong.Decode(result.Buffer.AsSpan(1));
          return RakNetServerInfo.Parse(pong.Motd);
        }

        // Ignore anything else and keep waiting for the pong.
      }
    }
    finally
    {
      await senderCts.CancelAsync();
      try
      {
        await senderTask;
      }
      catch (OperationCanceledException)
      {
        // Expected: the sender loop only stops via senderCts cancellation.
      }
    }
  }

  private static async Task ResendUntilCancelledAsync(UdpClient socket, byte[] payload, TimeSpan interval, CancellationToken ct)
  {
    while (true)
    {
      await socket.SendAsync(payload, ct);
      await Task.Delay(interval, ct);
    }
  }
}
