namespace BedrockConsoleClient.Auth.XboxLive;

internal sealed record MicrosoftTokenResult(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
