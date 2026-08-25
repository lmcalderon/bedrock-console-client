namespace BedrockConsoleClient.Networking.RakNet.Packets;

using BedrockConsoleClient.Networking.RakNet.IO;

internal readonly record struct ConnectedPong(long PingTime, long PongTime)
{
  public static byte[] Encode(long pingTime, long pongTime)
  {
    var buffer = new byte[17];
    buffer[0] = (byte)RakNetMessageId.ConnectedPong;
    var writer = new RakNetSpanWriter(buffer.AsSpan(1));
    writer.WriteInt64BE(pingTime);
    writer.WriteInt64BE(pongTime);
    return buffer;
  }

  public static ConnectedPong Decode(ReadOnlySpan<byte> data)
  {
    var reader = new RakNetSpanReader(data);
    long pingTime = reader.ReadInt64BE();
    long pongTime = reader.ReadInt64BE();
    return new ConnectedPong(pingTime, pongTime);
  }
}
