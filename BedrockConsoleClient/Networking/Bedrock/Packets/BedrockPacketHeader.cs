namespace BedrockConsoleClient.Networking.Bedrock.Packets;

using BedrockConsoleClient.Networking.Bedrock.IO;

internal static class BedrockPacketHeader
{
  // Packet ID occupies the low 10 bits of the header VarUInt32; the
  // remaining bits are sub-client IDs this client never uses (always 0).
  private const uint IdMask = 0x3FF;

  public static (BedrockPacketId Id, int HeaderSize) ReadId(ReadOnlySpan<byte> packet)
  {
    var reader = new BedrockVarIntReader(packet);
    uint header = reader.ReadVarUInt32();
    return ((BedrockPacketId)(header & IdMask), reader.Position);
  }
}
