namespace BedrockConsoleClient.Configuration;

public sealed record BedrockClientConfig
{
  // host:port of the Bedrock server to connect to.
  public required string ServerAddress { get; init; }

  // Max 16 chars; see docs/notes/bedrock-login-design.md. Ignored when
  // AuthMode is Microsoft - the signed-in Xbox gamertag is used instead.
  public required string Username { get; init; }

  public BedrockAuthMode AuthMode { get; init; } = BedrockAuthMode.SelfSigned;

  // Logs each packet's ID and size as it's sent/received.
  public bool Verbose { get; init; }
}
