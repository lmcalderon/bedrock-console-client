namespace BedrockConsoleClient.Configuration;

using System.Net;
using System.Net.Sockets;

internal static class ServerEndpointResolver
{
  public static async Task<IPEndPoint> ResolveAsync(string serverAddress, CancellationToken ct)
  {
    int separatorIndex = serverAddress.LastIndexOf(':');
    if (separatorIndex < 0)
    {
      throw new FormatException($"ServerAddress '{serverAddress}' must be in host:port form.");
    }

    string host = serverAddress[..separatorIndex];
    if (!ushort.TryParse(serverAddress[(separatorIndex + 1)..], out ushort port))
    {
      throw new FormatException($"ServerAddress '{serverAddress}' has an invalid port.");
    }

    if (IPAddress.TryParse(host, out IPAddress? literalAddress))
    {
      return new IPEndPoint(literalAddress, port);
    }

    IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct);
    if (addresses.Length == 0)
    {
      throw new SocketException((int)SocketError.HostNotFound);
    }

    return new IPEndPoint(addresses[0], port);
  }
}
