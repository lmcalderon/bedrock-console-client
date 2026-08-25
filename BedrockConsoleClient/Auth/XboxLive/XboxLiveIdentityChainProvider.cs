namespace BedrockConsoleClient.Auth.XboxLive;

using System.Security.Cryptography;
using BedrockConsoleClient.Networking.Bedrock.Identity;

// Signs in with a real Microsoft/Xbox Live account and produces the identity
// chain for online-mode servers - the Strategy counterpart to
// SelfSignedIdentityChainProvider. SignInAsync performs every step that
// doesn't depend on a specific connection (OAuth, device/SISU/XSTS tokens,
// PlayFab login, service discovery, the session token) up front and must run
// before the RakNet connection is opened, since interactive sign-in can take
// far longer than BedrockLoginOptions.LoginTimeout allows. ResolveAsync then
// performs the two remaining calls that are genuinely bound to a
// connection's fresh key pair: the identity chain (Certificate) and the
// multiplayer token (Token) - two separate fields in the Login packet's
// AuthInfoJson, confirmed from gophertunnel's login.request type; PMMP's
// currently-tested server only reads Token, but Certificate is sent anyway
// since other servers may expect it. See
// docs/notes/bedrock-xbox-live-auth-design.md.
internal sealed class XboxLiveIdentityChainProvider : IIdentityChainProvider
{
  private readonly HttpClient _client;
  private readonly XstsToken _minecraftToken;
  private readonly AuthorizationEnvironment _authEnvironment;
  private readonly string _sessionAuthorizationHeader;

  private XboxLiveIdentityChainProvider(HttpClient client, XstsToken minecraftToken, AuthorizationEnvironment authEnvironment, string sessionAuthorizationHeader)
  {
    _client = client;
    _minecraftToken = minecraftToken;
    _authEnvironment = authEnvironment;
    _sessionAuthorizationHeader = sessionAuthorizationHeader;
  }

  public static async Task<XboxLiveIdentityChainProvider> SignInAsync(HttpClient client, Action<string>? log, CancellationToken ct)
  {
    MicrosoftTokenCacheEntry? cached = MicrosoftTokenCache.TryLoad();

    ECDsa proofKey;
    MicrosoftTokenResult microsoft;
    if (cached is not null)
    {
      proofKey = cached.ProofKey;
      try
      {
        log?.Invoke("Refreshing cached Microsoft sign-in...");
        microsoft = await MicrosoftDeviceCodeAuth.RefreshAsync(client, cached.RefreshToken, ct);
      }
      catch (XboxLiveAuthException)
      {
        proofKey.Dispose();
        (proofKey, microsoft) = await SignInInteractiveAsync(client, log, ct);
      }
    }
    else
    {
      (proofKey, microsoft) = await SignInInteractiveAsync(client, log, ct);
    }

    XboxLiveDeviceToken device = await XboxLiveDeviceAuth.AuthenticateAsync(client, proofKey, ct);
    SisuAuthorizationResult sisu = await SisuAuthorization.AuthorizeAsync(client, proofKey, device, microsoft.AccessToken, ct);
    XstsToken minecraftToken = await XstsAuthorization.AuthorizeAsync(
        client, proofKey, device, sisu.TitleToken, sisu.UserToken, BedrockXboxLiveConfig.MinecraftRelyingParty, ct);

    string playFabSessionTicket = await PlayFabClient.LoginWithXboxAsync(client, proofKey, device, sisu.TitleToken, sisu.UserToken, sisu.DefaultRelyingPartyToken, ct);
    AuthorizationEnvironment authEnvironment = await MinecraftServiceDiscovery.DiscoverAuthEnvironmentAsync(client, ct);
    string sessionAuthorizationHeader = await MinecraftAuthorizationService.RequestSessionTokenAsync(client, authEnvironment, playFabSessionTicket, ct);

    MicrosoftTokenCache.Save(microsoft.RefreshToken, proofKey);
    log?.Invoke($"Signed in to Xbox Live as {sisu.DefaultRelyingPartyToken.GamerTag ?? sisu.DefaultRelyingPartyToken.UserHash}.");

    return new XboxLiveIdentityChainProvider(client, minecraftToken, authEnvironment, sessionAuthorizationHeader);
  }

  private static async Task<(ECDsa ProofKey, MicrosoftTokenResult Token)> SignInInteractiveAsync(HttpClient client, Action<string>? log, CancellationToken ct)
  {
    ECDsa proofKey = XboxLiveProofKey.Generate();
    MicrosoftDeviceCodeInfo deviceCode = await MicrosoftDeviceCodeAuth.RequestDeviceCodeAsync(client, ct);
    log?.Invoke($"Sign in to your Microsoft account at {deviceCode.VerificationUri} using the code {deviceCode.UserCode}.");
    MicrosoftTokenResult token = await MicrosoftDeviceCodeAuth.PollForAccessTokenAsync(client, deviceCode, ct);
    log?.Invoke("Microsoft sign-in complete.");
    return (proofKey, token);
  }

  public async Task<IdentityChainResult> ResolveAsync(BedrockKeyPair keyPair, CancellationToken ct)
  {
    string certificate = await MinecraftChainClient.RequestChainAsync(_client, _minecraftToken, keyPair, ct);
    string multiplayerToken = await MinecraftAuthorizationService.RequestMultiplayerTokenAsync(_client, _authEnvironment, _sessionAuthorizationHeader, keyPair, ct);

    // AuthenticationType.FULL, confirmed from PMMP source (see
    // docs/notes/bedrock-xbox-live-auth-design.md) - distinct from
    // SelfSignedIdentityChainProvider's SELF_SIGNED (2).
    return new IdentityChainResult(AuthenticationType: 0, Token: multiplayerToken, Certificate: certificate);
  }
}
