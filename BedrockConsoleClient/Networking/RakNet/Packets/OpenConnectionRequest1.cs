namespace BedrockConsoleClient.Networking.RakNet.Packets;

// Client -> server only; a real server would decode this, but this project
// never acts as one. The whole UDP payload is padded out to mtu-28 bytes
// (28 = 20-byte IP header + 8-byte UDP header) so its total on-the-wire size
// equals the MTU candidate being probed.
internal static class OpenConnectionRequest1
{
  public static byte[] Encode(byte clientProtocolVersion, int mtu)
  {
    var buffer = new byte[mtu - 28];
    buffer[0] = (byte)RakNetMessageId.OpenConnectionRequest1;
    RakNetMagic.Bytes.CopyTo(buffer.AsSpan(1));
    buffer[17] = clientProtocolVersion;
    return buffer;
  }
}
