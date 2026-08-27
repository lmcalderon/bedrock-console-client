namespace BedrockConsoleClient.Networking.Bedrock;

public sealed record BedrockLoginOptions
{
  public required string Username { get; init; }

  // Current Bedrock protocol version this client speaks, per
  // docs/notes/bedrock-login-design.md. Unlike Java Edition, Bedrock gives
  // no way to pin an older client version - the real game always runs
  // whatever Mojang shipped most recently, so this must track whatever the
  // actual local test server (official Bedrock Dedicated Server) speaks.
  // Confirmed live via a RakNet unconnected-ping MOTD reply from the local
  // BDS test server ("MCPE protocol 2168 (1.26.44)") - must be re-confirmed
  // the same way whenever the test server is updated.
  public uint ProtocolVersion { get; init; } = 2168;

  public string GameVersion { get; init; } = "1.26.44";

  // Cosmetic: included in client-data for informational purposes only, not
  // validated against the actual connection.
  public string ServerAddress { get; init; } = string.Empty;

  public TimeSpan LoginTimeout { get; init; } = TimeSpan.FromSeconds(15);
}
