namespace BedrockConsoleClient.Auth.XboxLive;

// An XASD (Xbox Authentication Services for Device) token - identifies this
// device to Xbox Live, bound to the proof key that signed the request that
// obtained it. Required before a SISU authorize or a direct XSTS request.
internal sealed record XboxLiveDeviceToken(string Token, DateTimeOffset NotAfter)
{
  public bool IsValid => Token.Length > 0 && DateTimeOffset.UtcNow < NotAfter;
}
