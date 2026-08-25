namespace BedrockConsoleClient.Networking.Bedrock;

public sealed record BedrockLoginOptions
{
  public required string Username { get; init; }

  // Current PMMP/Bedrock protocol version this client speaks, per
  // docs/notes/bedrock-login-design.md.
  public uint ProtocolVersion { get; init; } = 1001;

  public string GameVersion { get; init; } = "1.26.30";

  // Cosmetic: included in client-data for informational purposes only, not
  // validated by PMMP against the actual connection.
  public string ServerAddress { get; init; } = string.Empty;

  public TimeSpan LoginTimeout { get; init; } = TimeSpan.FromSeconds(15);
}
