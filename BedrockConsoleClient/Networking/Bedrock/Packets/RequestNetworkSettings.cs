namespace BedrockConsoleClient.Networking.Bedrock.Packets;

using BedrockConsoleClient.Networking.Bedrock.IO;

// Client -> server only. Always sent uncompressed, unencrypted, and alone in
// its own batch. It's the very first thing sent, before anything is negotiated.
internal static class RequestNetworkSettings
{
  public static byte[] Encode(uint protocolVersion)
  {
    var buffer = new byte[8];
    var writer = new BedrockVarIntWriter(buffer);
    writer.WriteVarUInt32((uint)BedrockPacketId.RequestNetworkSettings);
    writer.WriteUInt32BE(protocolVersion);
    return buffer[..writer.Position];
  }
}
