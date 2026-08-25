namespace BedrockConsoleClient.Auth.XboxLive;

internal sealed record MicrosoftDeviceCodeInfo(string DeviceCode, string UserCode, string VerificationUri, DateTimeOffset ExpiresAt, TimeSpan PollInterval);
