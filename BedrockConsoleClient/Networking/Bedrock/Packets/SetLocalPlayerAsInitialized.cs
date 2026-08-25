namespace BedrockConsoleClient.Networking.Bedrock.Packets;

using BedrockConsoleClient.Networking.Bedrock.IO;

// Client -> server only. The final packet of the login sequence; PMMP
// considers the player joined once this arrives.
internal static class SetLocalPlayerAsInitialized
{
  public static byte[] Encode(ulong actorRuntimeId)
  {
    var buffer = new byte[15];
    var writer = new BedrockVarIntWriter(buffer);
    writer.WriteVarUInt32((uint)BedrockPacketId.SetLocalPlayerAsInitialized);
    writer.WriteVarUInt64(actorRuntimeId);
    return buffer[..writer.Position];
  }
}
