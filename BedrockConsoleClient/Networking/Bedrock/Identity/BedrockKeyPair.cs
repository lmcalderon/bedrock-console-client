namespace BedrockConsoleClient.Networking.Bedrock.Identity;

using System.Security.Cryptography;

// A throwaway P-384 keypair generated fresh per connection. This client has
// no persistent identity in offline mode. The same key material serves two
// roles: signing the self-signed identity/client-data JWTs (ECDsa) and the
// encryption handshake's ECDH exchange (ECDiffieHellman). Bedrock doesn't
// generate a separate key for either purpose, confirmed from PMMP/gophertunnel.
public sealed class BedrockKeyPair : IDisposable
{
  private readonly ECParameters _parameters;

  public ECDsa Signing { get; }

  // Base64 (standard, not Base64Url) of the DER SubjectPublicKeyInfo -
  // exactly the x5u/cpk claim value both the identity chain and the
  // encryption handshake expect.
  public string PublicKeyBase64Der { get; }

  private BedrockKeyPair(ECDsa signing, ECParameters parameters, string publicKeyBase64Der)
  {
    Signing = signing;
    _parameters = parameters;
    PublicKeyBase64Der = publicKeyBase64Der;
  }

  public static BedrockKeyPair Generate()
  {
    var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);
    var parameters = ecdsa.ExportParameters(includePrivateParameters: true);
    string publicKeyDer = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
    return new BedrockKeyPair(ecdsa, parameters, publicKeyDer);
  }

  // A fresh ECDiffieHellman view of the same key material, for the
  // encryption handshake's shared-secret derivation. Caller disposes.
  public ECDiffieHellman CreateDiffieHellman()
  {
    var ecdh = ECDiffieHellman.Create();
    ecdh.ImportParameters(_parameters);
    return ecdh;
  }

  public void Dispose() => Signing.Dispose();
}
