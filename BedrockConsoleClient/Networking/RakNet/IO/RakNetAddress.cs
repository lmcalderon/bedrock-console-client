namespace BedrockConsoleClient.Networking.RakNet.IO;

using System.Buffers.Binary;
using System.Net;

// RakNet's wire address format bit-flips (XORs with 0xFF) every IPv4 octet before
// writing it. A historical quirk from the original C++ implementation, not real
// obfuscation. Only IPv4 encode is implemented: this project only ever dials an
// IPv4 loopback test server. Decode handles both IPv4 and IPv6 defensively so an
// unexpected system-address entry from the server doesn't crash parsing.
internal static class RakNetAddress
{
  private const int SizeV4 = 1 + 4 + 2;
  private const int SizeV6 = 1 + 2 + 2 + 4 + 16 + 4;

  public static int WriteIPv4(Span<byte> destination, IPEndPoint endpoint)
  {
    Span<byte> octets = stackalloc byte[4];
    if (!endpoint.Address.TryWriteBytes(octets, out int written) || written != 4)
    {
      throw new ArgumentException("Expected an IPv4 endpoint.", nameof(endpoint));
    }

    destination[0] = 4;
    destination[1] = (byte)~octets[0];
    destination[2] = (byte)~octets[1];
    destination[3] = (byte)~octets[2];
    destination[4] = (byte)~octets[3];
    BinaryPrimitives.WriteUInt16BigEndian(destination[5..], (ushort)endpoint.Port);
    return SizeV4;
  }

  // Matches go-raknet's zero-value netip.AddrPort encoding: used for unused
  // "system address" slots that this milestone doesn't populate with anything real.
  public static int WritePlaceholder(Span<byte> destination)
  {
    destination[0] = 4;
    destination[1] = 255;
    destination[2] = 255;
    destination[3] = 255;
    destination[4] = 255;
    destination[5] = 0;
    destination[6] = 0;
    return SizeV4;
  }

  public static IPEndPoint Read(ReadOnlySpan<byte> data, out int consumed)
  {
    byte version = data[0];
    if (version is 4 or 0)
    {
      Span<byte> octets = [(byte)~data[1], (byte)~data[2], (byte)~data[3], (byte)~data[4]];
      ushort port = BinaryPrimitives.ReadUInt16BigEndian(data[5..]);
      consumed = SizeV4;
      return new IPEndPoint(new IPAddress(octets), port);
    }

    // IPv6 layout: version(1) + family(2, ignored) + port(2, BE) + flow info(4, ignored)
    // + address(16) + scope id(4, ignored).
    ushort port6 = BinaryPrimitives.ReadUInt16BigEndian(data[3..]);
    var addressBytes = data.Slice(9, 16).ToArray();
    consumed = SizeV6;
    return new IPEndPoint(new IPAddress(addressBytes), port6);
  }

  public static int SizeOf(ReadOnlySpan<byte> data) => data.Length == 0 || data[0] is 4 or 0 ? SizeV4 : SizeV6;
}
