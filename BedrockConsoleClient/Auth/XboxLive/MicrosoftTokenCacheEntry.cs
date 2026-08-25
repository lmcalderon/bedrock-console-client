namespace BedrockConsoleClient.Auth.XboxLive;

using System.Security.Cryptography;

internal sealed record MicrosoftTokenCacheEntry(string RefreshToken, ECDsa ProofKey);
