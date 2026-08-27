namespace BedrockConsoleClient.Networking.RakNet.Packets;

using System.Buffers.Binary;
using System.Net;
using BedrockConsoleClient.Networking.RakNet.IO;

// Client -> server only. Carries the anti-amplification cookie back only if
// OpenConnectionReply1 indicated the server requires it. The cookie flag
// itself is not re-transmitted in this packet, only the raw cookie value.
//
// Servers that require a cookie detect its presence by the packet's
// remaining length rather than a flag byte (go-raknet's version of this
// packet didn't match this), and expect cookie(4) + a 1-byte "encryption
// challenge" placeholder (5 bytes total), not just the bare 4-byte cookie.
// Confirmed empirically against real BDS: the handshake reaches Connected
// against it. This client never uses libcat security, so the challenge byte
// is always 0.
internal static class OpenConnectionRequest2
{
  public static byte[] Encode(IPEndPoint serverAddress, ushort mtu, long clientGuid, bool serverHasSecurity, uint cookie)
  {
    int cookieRegionSize = serverHasSecurity ? 5 : 0;
    const int addressSize = 7; // IPv4 only - see RakNetAddress.
    var buffer = new byte[1 + 16 + cookieRegionSize + addressSize + 2 + 8];

    buffer[0] = (byte)RakNetMessageId.OpenConnectionRequest2;
    RakNetMagic.Bytes.CopyTo(buffer.AsSpan(1));
    int offset = 17;
    if (serverHasSecurity)
    {
      BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), cookie);
      offset += 4;
      buffer[offset] = 0; // encryption challenge placeholder
      offset += 1;
    }

    offset += RakNetAddress.WriteIPv4(buffer.AsSpan(offset), serverAddress);

    var writer = new RakNetSpanWriter(buffer.AsSpan(offset));
    writer.WriteUInt16BE(mtu);
    writer.WriteInt64BE(clientGuid);
    return buffer;
  }
}
