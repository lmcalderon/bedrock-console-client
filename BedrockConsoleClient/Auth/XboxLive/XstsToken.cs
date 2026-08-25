namespace BedrockConsoleClient.Auth.XboxLive;

// An XSTS (Xbox Secure Token Service) token, scoped to one relying party.
// The same shape serves both the default "http://xboxlive.com" token SISU
// authorize returns (GamerTag/Xuid populated) and the "https://multiplayer.
// minecraft.net/" token requested separately for the identity chain request
// (GamerTag/Xuid absent per go-xsapi's xsts.UserInfo doc comments - those
// fields are only present for the default relying party).
internal sealed record XstsToken(string Token, string UserHash, DateTimeOffset NotAfter, string? GamerTag, string? Xuid)
{
  public bool IsValid => Token.Length > 0 && UserHash.Length > 0 && DateTimeOffset.UtcNow < NotAfter;

  // Wire format Xbox Live services expect in the 'Authorization' header,
  // confirmed from go-xsapi's xsts.Token.String.
  public string AuthorizationHeaderValue => $"XBL3.0 x={UserHash};{Token}";
}
