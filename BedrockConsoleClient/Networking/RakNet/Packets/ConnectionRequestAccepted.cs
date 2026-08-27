namespace BedrockConsoleClient.Networking.RakNet.Packets;

using System.Net;
using BedrockConsoleClient.Networking.RakNet.IO;

// Server -> client only.
internal readonly record struct ConnectionRequestAccepted(
    IPEndPoint ClientAddress,
    ushort SystemIndex,
    long PingTime,
    long PongTime)
{
  private const int SystemAddressSlotCount = 20;

  public static ConnectionRequestAccepted Decode(ReadOnlySpan<byte> data)
  {
    var clientAddress = RakNetAddress.Read(data, out int consumed);
    var reader = new RakNetSpanReader(data[consumed..]);
    ushort systemIndex = reader.ReadUInt16BE();

    // The system-address list is nominally 20 entries, but real servers often
    // send fewer. Stop once exactly 16 bytes remain (the two trailing int64
    // timestamps) rather than trusting a fixed count.
    for (int i = 0; i < SystemAddressSlotCount && reader.Remaining != 16; i++)
    {
      int addressSize = RakNetAddress.SizeOf(reader.ReadRemaining());
      reader.Advance(addressSize);
    }

    long pingTime = reader.ReadInt64BE();
    long pongTime = reader.ReadInt64BE();
    return new ConnectionRequestAccepted(clientAddress, systemIndex, pingTime, pongTime);
  }
}
