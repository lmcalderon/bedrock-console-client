namespace BedrockConsoleClient.Networking.RakNet.Packets;

using System.Net;
using BedrockConsoleClient.Networking.RakNet.IO;

// Client -> server only. Mirrors the 20-slot system-address list back with
// placeholder addresses, matching go-raknet's behavior when it has no real
// alternate addresses to report (this client only ever has one).
internal static class NewIncomingConnection
{
  private const int SystemAddressSlotCount = 20;
  private const int AddressSize = 7; // IPv4 only.

  public static byte[] Encode(IPEndPoint serverAddress, long pingTime, long pongTime)
  {
    var buffer = new byte[1 + AddressSize + (SystemAddressSlotCount * AddressSize) + 16];
    buffer[0] = (byte)RakNetMessageId.NewIncomingConnection;
    int offset = 1 + RakNetAddress.WriteIPv4(buffer.AsSpan(1), serverAddress);
    for (int i = 0; i < SystemAddressSlotCount; i++)
    {
      offset += RakNetAddress.WritePlaceholder(buffer.AsSpan(offset));
    }

    var writer = new RakNetSpanWriter(buffer.AsSpan(offset));
    writer.WriteInt64BE(pingTime);
    writer.WriteInt64BE(pongTime);
    return buffer;
  }
}
