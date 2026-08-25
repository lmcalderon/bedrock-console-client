namespace BedrockConsoleClient.Networking.Bedrock.Packets;

using BedrockConsoleClient.Networking.Bedrock.IO;

// Client -> server only. Empty payload; a pure signal that the client has
// derived the shared key and is switching its own cipher on. Sent through
// the now-encrypting send path (the caller enables encryption before this).
internal static class ClientToServerHandshake
{
  public static byte[] Encode()
  {
    var buffer = new byte[5];
    var writer = new BedrockVarIntWriter(buffer);
    writer.WriteVarUInt32((uint)BedrockPacketId.ClientToServerHandshake);
    return buffer[..writer.Position];
  }
}
