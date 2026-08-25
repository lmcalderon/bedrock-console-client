namespace BedrockConsoleClient.Configuration;

public sealed record BedrockClientConfig
{
  // host:port of the Bedrock server to connect to.
  public required string ServerAddress { get; init; }

  // Bedrock usernames are capped at 16 chars, alphanumeric/underscore/space
  // only (Player::isValidUserName on the PMMP side; see
  // docs/notes/bedrock-login-design.md).
  public required string Username { get; init; }
}
