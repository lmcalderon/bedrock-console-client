namespace BedrockConsoleClient.Networking.Bedrock.Packets;

using BedrockConsoleClient.Networking.Bedrock.IO;

internal enum ResourcePackClientResponseStatus : byte
{
  Refused = 1,
  SendPacks = 2,
  HaveAllPacks = 3,
  Completed = 4,
}

// Client -> server only. Status is a raw byte, not a VarInt.
internal static class ResourcePackClientResponse
{
  public static byte[] Encode(ResourcePackClientResponseStatus status)
  {
    var buffer = new byte[8];
    var writer = new BedrockVarIntWriter(buffer);
    writer.WriteVarUInt32((uint)BedrockPacketId.ResourcePackClientResponse);
    writer.WriteByte((byte)status);
    writer.WriteUInt16LE(0); // 0 pack IDs - this milestone assumes no configured packs
    return buffer[..writer.Position];
  }
}
