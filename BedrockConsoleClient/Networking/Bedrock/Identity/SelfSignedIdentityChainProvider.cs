namespace BedrockConsoleClient.Networking.Bedrock.Identity;

// Wraps Milestone 1's offline-mode identity chain as an
// IIdentityChainProvider, unchanged from before this seam existed.
internal sealed class SelfSignedIdentityChainProvider(string username) : IIdentityChainProvider
{
  // The dummy chain gophertunnel's login.EncodeOffline sends for
  // AuthenticationType.SELF_SIGNED: a chain with one empty-string element,
  // never a real signed JWT and never omitted - see
  // IdentityChainResult.Certificate.
  private const string DummyCertificateChain = "{\"chain\":[\"\"]}";

  public Task<IdentityChainResult> ResolveAsync(BedrockKeyPair keyPair, CancellationToken ct)
  {
    string token = SelfSignedIdentityChain.Build(keyPair, username);

    return Task.FromResult(new IdentityChainResult(2, token, DummyCertificateChain));
  }
}
