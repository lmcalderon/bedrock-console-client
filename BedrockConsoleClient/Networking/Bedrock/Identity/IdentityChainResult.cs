namespace BedrockConsoleClient.Networking.Bedrock.Identity;

// Certificate is the JSON-encoded {"chain":[...]} identity chain string, as
// the real Bedrock wire format expects it (a string, not a nested object) -
// present (a dummy {"chain":[""]}) for self-signed logins, omitted entirely
// for a real Xbox Live login (see XboxLiveIdentityChainProvider). Ordering
// this field before AuthenticationType/Token in the serialized JSON matters
// to real BDS - see BedrockSession.SendLoginAsync and
// docs/notes/bedrock-login-design.md.
public sealed record IdentityChainResult(int AuthenticationType, string Token, string? Certificate = null);
