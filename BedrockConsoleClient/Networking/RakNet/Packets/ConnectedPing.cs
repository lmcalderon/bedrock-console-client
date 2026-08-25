namespace BedrockConsoleClient.Networking.RakNet.Packets;

using BedrockConsoleClient.Networking.RakNet.IO;

// Bidirectional: this client sends its own keep-alive pings and must also
// reply to pings the server sends.
internal static class ConnectedPing
{
  public static byte[] Encode(long pingTime)
  {
    var buffer = new byte[9];
    buffer[0] = (byte)RakNetMessageId.ConnectedPing;
    var writer = new RakNetSpanWriter(buffer.AsSpan(1));
    writer.WriteInt64BE(pingTime);
    return buffer;
  }

  public static long Decode(ReadOnlySpan<byte> data)
  {
    var reader = new RakNetSpanReader(data);
    return reader.ReadInt64BE();
  }
}
