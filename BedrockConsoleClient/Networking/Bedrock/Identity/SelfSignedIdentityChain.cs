namespace BedrockConsoleClient.Networking.Bedrock.Identity;

// Builds the "Token" JWT inside authInfoJson for offline-mode login. Field
// set and order were both wrong in every previous attempt at this file (see
// git history) - the actual fix, found by running gophertunnel itself
// against this project's own BDS test container and capturing its real,
// successful Login packet: real BDS's JSON parsing here is order-sensitive.
// gophertunnel's go-jose library serializes JWT claims through a merged map,
// which Go's encoding/json always emits in alphabetical key order - so that
// literal alphabetical order (not any particular "natural" declaration
// order) is what has to be reproduced. The exact field set is aud/cpk/exp/
// ipt/leguuid/mid/nbf/tid/xid/xname - no ap/nid/nname/pid/pname (those were
// from an unrelated schema this project mistakenly mixed in earlier). See
// docs/notes/bedrock-login-design.md.
internal static class SelfSignedIdentityChain
{
  public static string Build(BedrockKeyPair keyPair, string username)
  {
    var header = new { alg = "ES384", x5u = keyPair.PublicKeyBase64Der };
    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var payload = new
    {
      aud = "api://auth-minecraft-services/multiplayer",
      cpk = keyPair.PublicKeyBase64Der,
      exp = now + (3600 * 6),
      ipt = string.Empty,
      leguuid = Guid.NewGuid().ToString(),
      mid = string.Empty,
      nbf = now - (3600 * 6),
      tid = string.Empty,
      xid = string.Empty,
      xname = username,
    };
    return JwtSigner.Sign(header, payload, keyPair.Signing);
  }
}
