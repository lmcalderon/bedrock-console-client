namespace BedrockConsoleClient.Networking.Bedrock.Identity;

// Builds the "Token" JWT inside authInfoJson for offline-mode login. Claim
// names verified against PMMP's current SelfSignedJwtBody mapper (not the
// "identity" naming some JS reference clients' legacy code path uses; PMMP
// reads leguuid, not identity). First attempt was missing "mid"; PMMP's own
// error message named the exact missing property, confirmed against the real
// server rather than guessed twice. See docs/notes/bedrock-login-design.md.
internal static class SelfSignedIdentityChain
{
  public static string Build(BedrockKeyPair keyPair, string username)
  {
    var header = new { alg = "ES384", x5u = keyPair.PublicKeyBase64Der };
    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var payload = new
    {
      leguuid = Guid.NewGuid().ToString(),
      xname = username,
      cpk = keyPair.PublicKeyBase64Der,
      mid = Guid.NewGuid().ToString("N"),
      ap = 0,
      nid = string.Empty,
      nname = string.Empty,
      pid = string.Empty,
      pname = string.Empty,
      xid = string.Empty,
      nbf = now,
      exp = now + 3600,

      // Strictly required, not optional as first assumed. PMMP's
      // AuthJwtHelper checks this unconditionally against MOJANG_AUDIENCE,
      // confirmed from source after "Invalid JWT audience" from a real run.
      aud = "api://auth-minecraft-services/multiplayer",
    };
    return JwtSigner.Sign(header, payload, keyPair.Signing);
  }
}
