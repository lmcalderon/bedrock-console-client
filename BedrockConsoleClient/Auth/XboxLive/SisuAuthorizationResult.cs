namespace BedrockConsoleClient.Auth.XboxLive;

internal sealed record SisuAuthorizationResult(XboxLiveTitleToken TitleToken, XboxLiveUserToken UserToken, XstsToken DefaultRelyingPartyToken);
