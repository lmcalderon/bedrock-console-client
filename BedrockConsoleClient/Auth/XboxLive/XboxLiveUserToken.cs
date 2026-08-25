namespace BedrockConsoleClient.Auth.XboxLive;

// An XASU (Xbox Authentication Services for User) token - identifies the
// signed-in Microsoft account to Xbox Live, exchanged from the OAuth access
// token during SISU authorize.
internal sealed record XboxLiveUserToken(string Token, DateTimeOffset NotAfter)
{
  public bool IsValid => Token.Length > 0 && DateTimeOffset.UtcNow < NotAfter;
}
