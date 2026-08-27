namespace BedrockConsoleClient.Networking.RakNet.Packets;

using BedrockConsoleClient.Networking.RakNet.IO;

// Client -> server only. Sent standalone (not as part of a connection
// attempt) to read a server's advertised MOTD - see UnconnectedPong.
internal static class UnconnectedPing
{
  public static byte[] Encode(long clientGuid)
  {
    var buffer = new byte[33];
    buffer[0] = (byte)RakNetMessageId.UnconnectedPing;
    var writer = new RakNetSpanWriter(buffer.AsSpan(1));
    writer.WriteInt64BE(Environment.TickCount64);
    writer.WriteBytes(RakNetMagic.Bytes);
    writer.WriteInt64BE(clientGuid);
    return buffer;
  }
}
