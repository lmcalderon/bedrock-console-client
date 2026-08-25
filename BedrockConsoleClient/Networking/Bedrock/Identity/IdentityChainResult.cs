namespace BedrockConsoleClient.Networking.Bedrock.Identity;

// Certificate is the JSON-encoded {"chain":[...]} identity chain string, as
// the real Bedrock wire format expects it (a string, not a nested object) -
// confirmed from gophertunnel's login.request.MarshalJSON. Null for
// self-signed mode, which has no chain to present.
public sealed record IdentityChainResult(int AuthenticationType, string Token, string? Certificate = null);
