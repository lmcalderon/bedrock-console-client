namespace BedrockConsoleClient.Networking.Bedrock.Identity;

// The Strategy seam between BedrockSession and however the Login packet's
// identity chain gets produced. Deliberately not introduced until a second
// implementation existed (Xbox Live) to justify it; see
// docs/notes/bedrock-xbox-live-auth-design.md.
public interface IIdentityChainProvider
{
  Task<IdentityChainResult> ResolveAsync(BedrockKeyPair keyPair, CancellationToken ct);
}
