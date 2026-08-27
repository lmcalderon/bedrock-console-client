namespace BedrockConsoleClient.Networking.RakNet.Packets;

using System.Text;
using BedrockConsoleClient.Networking.RakNet.IO;

// Server -> client only. data excludes the leading message-ID byte (caller
// strips that); the 16-byte magic prefix is still present and skipped here.
internal readonly record struct UnconnectedPong(long Time, long ServerGuid, string Motd)
{
  public static UnconnectedPong Decode(ReadOnlySpan<byte> data)
  {
    var reader = new RakNetSpanReader(data);
    long time = reader.ReadInt64BE();
    long serverGuid = reader.ReadInt64BE();
    reader.Advance(16); // magic
    ushort motdLength = reader.ReadUInt16BE();
    string motd = Encoding.UTF8.GetString(reader.ReadBytes(motdLength));
    return new UnconnectedPong(time, serverGuid, motd);
  }
}
