namespace BedrockConsoleClient.Networking.Bedrock.Packets;

using BedrockConsoleClient.Networking.Bedrock.IO;

internal enum PlayStatusCode : uint
{
  LoginSuccess = 0,
  LoginFailedClient = 1,
  LoginFailedServer = 2,
  PlayerSpawn = 3,
  LoginFailedInvalidTenant = 4,
  LoginFailedVanillaEdu = 5,
  LoginFailedEduVanilla = 6,
  LoginFailedServerFull = 7,
}

// Server -> client only.
internal static class PlayStatus
{
  public static PlayStatusCode Decode(ReadOnlySpan<byte> payload)
  {
    var reader = new BedrockVarIntReader(payload);
    return (PlayStatusCode)reader.ReadUInt32BE();
  }
}
