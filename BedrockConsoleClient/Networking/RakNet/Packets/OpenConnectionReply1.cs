namespace BedrockConsoleClient.Networking.RakNet.Packets;

using BedrockConsoleClient.Networking.RakNet.IO;

// Server -> client only. data excludes the leading message-ID byte (caller
// strips that); the 16-byte magic prefix is still present and skipped here.
internal readonly record struct OpenConnectionReply1(
    long ServerGuid,
    bool ServerHasSecurity,
    uint Cookie,
    ushort Mtu)
{
  public static OpenConnectionReply1 Decode(ReadOnlySpan<byte> data)
  {
    var reader = new RakNetSpanReader(data);
    reader.Advance(16); // magic
    long serverGuid = reader.ReadInt64BE();
    bool serverHasSecurity = reader.ReadBool();
    uint cookie = 0;
    if (serverHasSecurity)
    {
      cookie = reader.ReadUInt32BE();
    }

    ushort mtu = reader.ReadUInt16BE();
    return new OpenConnectionReply1(serverGuid, serverHasSecurity, cookie, mtu);
  }
}
