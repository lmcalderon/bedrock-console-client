namespace BedrockConsoleClient.Auth.XboxLive;

// Mirrors go-xsapi's xal/internal.SignaturePolicy. Most Xbox Live
// authentication endpoints (device/title/user/XSTS token requests) use the
// fixed Default policy; only the SISU authorize endpoint has its policy
// resolved dynamically, from NSAL title data. See
// docs/notes/bedrock-xbox-live-auth-design.md.
internal readonly record struct XboxLiveSignaturePolicy(uint Version, int MaxBodyBytes, IReadOnlyList<string> ExtraHeaders)
{
  public static readonly XboxLiveSignaturePolicy Default = new(Version: 1, MaxBodyBytes: 0, ExtraHeaders: []);
}
