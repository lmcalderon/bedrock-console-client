namespace BedrockConsoleClient.Networking.Bedrock.Identity;

// Wraps Milestone 1's offline-mode identity chain as an
// IIdentityChainProvider, unchanged from before this seam existed.
internal sealed class SelfSignedIdentityChainProvider(string username) : IIdentityChainProvider
{
  public Task<IdentityChainResult> ResolveAsync(BedrockKeyPair keyPair, CancellationToken ct)
  {
    string token = SelfSignedIdentityChain.Build(keyPair, username);

    // AuthenticationType numeric value for self-signed/offline mode is not
    // literally confirmed from PMMP source; best-guess 2, from reference
    // client convention. See docs/notes/bedrock-login-design.md.
    return Task.FromResult(new IdentityChainResult(2, token));
  }
}
