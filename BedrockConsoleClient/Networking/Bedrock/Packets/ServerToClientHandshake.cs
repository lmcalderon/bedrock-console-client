namespace BedrockConsoleClient.Networking.Bedrock.Packets;

using BedrockConsoleClient.Networking.Bedrock.IO;

// Server -> client only. Single VarUInt32-length-prefixed string: a compact
// JWS carrying the server's ECDH public key and the encryption salt. Sent
// unencrypted. Encryption isn't active until this handshake completes.
internal static class ServerToClientHandshake
{
  public static string DecodeJwt(ReadOnlySpan<byte> payload)
  {
    var reader = new BedrockVarIntReader(payload);
    return reader.ReadString();
  }
}
