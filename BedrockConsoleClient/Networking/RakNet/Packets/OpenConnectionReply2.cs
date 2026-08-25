namespace BedrockConsoleClient.Networking.RakNet.Packets;

using System.Net;
using BedrockConsoleClient.Networking.RakNet.IO;

// Server -> client only. data excludes the leading message-ID byte (caller
// strips that); the 16-byte magic prefix is still present and skipped here.
internal readonly record struct OpenConnectionReply2(
    long ServerGuid,
    IPEndPoint ClientAddress,
    ushort Mtu,
    bool EncryptionEnabled)
{
  public static OpenConnectionReply2 Decode(ReadOnlySpan<byte> data)
  {
    var reader = new RakNetSpanReader(data);
    reader.Advance(16); // magic
    long serverGuid = reader.ReadInt64BE();
    var clientAddress = RakNetAddress.Read(reader.ReadRemaining(), out int consumed);
    reader.Advance(consumed);
    ushort mtu = reader.ReadUInt16BE();
    bool encryptionEnabled = reader.ReadBool();
    return new OpenConnectionReply2(serverGuid, clientAddress, mtu, encryptionEnabled);
  }
}
