namespace BedrockConsoleClient.Networking.Bedrock.Packets;

using System.Text;
using BedrockConsoleClient.Networking.Bedrock.IO;

// Client -> server only. connectionRequest's inner two lengths are LE UInt32,
// not VarInt, a real exception nested inside the VarInt-length-prefixed
// outer string, confirmed against LoginPacket::encodeConnectionRequest.
internal static class Login
{
  public static byte[] Encode(uint protocolVersion, string authInfoJson, string clientDataJwt)
  {
    byte[] authInfoBytes = Encoding.UTF8.GetBytes(authInfoJson);
    byte[] clientDataBytes = Encoding.UTF8.GetBytes(clientDataJwt);
    int connectionRequestLength = 4 + authInfoBytes.Length + 4 + clientDataBytes.Length;

    var connectionRequest = new byte[connectionRequestLength];
    var crWriter = new BedrockVarIntWriter(connectionRequest);
    crWriter.WriteUInt32LE((uint)authInfoBytes.Length);
    crWriter.WriteBytes(authInfoBytes);
    crWriter.WriteUInt32LE((uint)clientDataBytes.Length);
    crWriter.WriteBytes(clientDataBytes);

    var buffer = new byte[5 + 4 + 5 + connectionRequestLength];
    var writer = new BedrockVarIntWriter(buffer);
    writer.WriteVarUInt32((uint)BedrockPacketId.Login);
    writer.WriteUInt32BE(protocolVersion);
    writer.WriteVarUInt32((uint)connectionRequestLength);
    writer.WriteBytes(connectionRequest);
    return buffer[..writer.Position];
  }
}
