namespace BedrockConsoleClient.Auth.XboxLive;

// The device identity Minecraft: Bedrock Edition for Android presents to
// Xbox Live. Confirmed from gophertunnel's AndroidConfig
// (minecraft/auth/xbox.go). Android is used rather than a desktop identity
// because gophertunnel's own Win32Config is documented as non-functional -
// "retrieving the RPS ticket required for device token requests is not yet
// known" - so impersonating a mobile device is the only proven-working path
// available, not a shortcut unique to this project. See
// docs/notes/bedrock-xbox-live-auth-design.md.
internal static class BedrockXboxLiveConfig
{
  public const string ClientId = "0000000048183522";
  public const string UserAgent = "XAL Android 2025.04.20250326.000";
  public const long TitleId = 1739947436;
  public const string Sandbox = "RETAIL";
  public const string DeviceType = "Android";
  public const string DeviceVersion = "13";

  // The scope requested for the initial Microsoft OAuth token. Confirmed
  // from go-xsapi's xal/sisu/oauth2.go.
  public const string OAuthScope = "service::user.auth.xboxlive.com::MBI_SSL";

  public const string DefaultRelyingParty = "http://xboxlive.com";

  // PlayFab title ID for retail Minecraft: Bedrock Edition. Confirmed from
  // gophertunnel's service.AuthorizationEnvironment.PlayFabTitleID doc
  // comment ("typically '20CA2', and this is not something that could be
  // easily changed").
  public const string PlayFabTitleId = "20CA2";

  // The client-version string paired with BedrockLoginOptions.ProtocolVersion
  // (2168), sent to the Minecraft service discovery/authorization endpoints.
  // Confirmed live via a RakNet unconnected-ping MOTD reply from the local
  // BDS test server - must be updated together with ProtocolVersion if that
  // ever changes.
  public const string ClientVersion = "1.26.44";
}
