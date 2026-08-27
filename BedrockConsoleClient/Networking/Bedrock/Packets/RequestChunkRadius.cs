namespace BedrockConsoleClient.Networking.Bedrock.Packets;

using BedrockConsoleClient.Networking.Bedrock.IO;

// Client -> server only. Sent once after ItemRegistry, as part of the real
// spawn sequence - see BedrockLoginState. ChunkRadius is a signed VarInt
// (zigzag), MaxChunkRadius a plain byte - confirmed from gophertunnel's
// RequestChunkRadius.Marshal (io.Varint32 + io.Uint8). The actual values
// don't matter for an idle client that never renders chunks; 16 mirrors what
// a real client sends by default.
internal static class RequestChunkRadius
{
  public static byte[] Encode(int chunkRadius = 16, byte maxChunkRadius = 16)
  {
    var buffer = new byte[16];
    var writer = new BedrockVarIntWriter(buffer);
    writer.WriteVarUInt32((uint)BedrockPacketId.RequestChunkRadius);
    writer.WriteVarInt32(chunkRadius);
    writer.WriteByte(maxChunkRadius);
    return buffer[..writer.Position];
  }
}
