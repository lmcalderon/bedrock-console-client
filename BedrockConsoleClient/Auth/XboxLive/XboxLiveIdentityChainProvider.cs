namespace BedrockConsoleClient.Auth.XboxLive;

using System.Security.Cryptography;
using BedrockConsoleClient.Networking.Bedrock.Identity;

// Signs in with a real Microsoft/Xbox Live account and produces the Login
// packet's AuthInfoJson for online-mode servers - the Strategy counterpart to
// SelfSignedIdentityChainProvider. SignInAsync performs every step that
// doesn't depend on a specific connection (OAuth, device/SISU/XSTS tokens,
// PlayFab login, service discovery, the session token) up front and must run
// before the RakNet connection is opened, since interactive sign-in can take
// far longer than BedrockLoginOptions.LoginTimeout allows. ResolveAsync then
// performs the one remaining call genuinely bound to a connection's fresh key
// pair: the multiplayer token (Token). No Certificate field is sent - a real
// client's Login packet AuthInfoJson for this AuthenticationType has only
// AuthenticationType and Token, confirmed from a packet capture of a real
// client's login against this project's own target server (an earlier
// implementation that fetched a chain from multiplayer.minecraft.net/
// authentication and added a self-signed bridge JWT, modeled on
// gophertunnel's login.Encode, was rejected by the real server outright).
// See docs/notes/bedrock-xbox-live-auth-design.md.
internal sealed class XboxLiveIdentityChainProvider : IIdentityChainProvider
{
  private readonly HttpClient _client;
  private readonly AuthorizationEnvironment _authEnvironment;
  private readonly string _sessionAuthorizationHeader;

  private XboxLiveIdentityChainProvider(HttpClient client, AuthorizationEnvironment authEnvironment, string sessionAuthorizationHeader)
  {
    _client = client;
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

    string playFabSessionTicket = await PlayFabClient.LoginWithXboxAsync(client, proofKey, device, sisu.TitleToken, sisu.UserToken, sisu.DefaultRelyingPartyToken, ct);
    AuthorizationEnvironment authEnvironment = await MinecraftServiceDiscovery.DiscoverAuthEnvironmentAsync(client, ct);
    string sessionAuthorizationHeader = await MinecraftAuthorizationService.RequestSessionTokenAsync(client, authEnvironment, playFabSessionTicket, ct);

    MicrosoftTokenCache.Save(microsoft.RefreshToken, proofKey);
    log?.Invoke($"Signed in to Xbox Live as {sisu.DefaultRelyingPartyToken.GamerTag ?? sisu.DefaultRelyingPartyToken.UserHash}.");

    return new XboxLiveIdentityChainProvider(client, authEnvironment, sessionAuthorizationHeader);
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
    string multiplayerToken = await MinecraftAuthorizationService.RequestMultiplayerTokenAsync(_client, _authEnvironment, _sessionAuthorizationHeader, keyPair, ct);

    // AuthenticationType.FULL (see docs/notes/bedrock-xbox-live-auth-design.md)
    // - distinct from SelfSignedIdentityChainProvider's SELF_SIGNED (2).
    return new IdentityChainResult(AuthenticationType: 0, Token: multiplayerToken);
  }
}
