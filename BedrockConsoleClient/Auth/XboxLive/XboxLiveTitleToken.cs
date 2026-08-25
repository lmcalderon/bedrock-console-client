namespace BedrockConsoleClient.Auth.XboxLive;

// An XAST (Xbox Authentication Services for Title) token - identifies this
// title (Minecraft: Bedrock Edition) to Xbox Live, issued by SISU authorize
// alongside the user token.
internal sealed record XboxLiveTitleToken(string Token, DateTimeOffset NotAfter)
{
  public bool IsValid => Token.Length > 0 && DateTimeOffset.UtcNow < NotAfter;
}
