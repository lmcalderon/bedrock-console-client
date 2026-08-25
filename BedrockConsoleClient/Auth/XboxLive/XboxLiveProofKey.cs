namespace BedrockConsoleClient.Auth.XboxLive;

using System.Buffers.Text;
using System.Security.Cryptography;

// The proof key that signs every Xbox Live authentication request (see
// XboxLiveRequestSigner). Deliberately a separate P-256 key from
// BedrockKeyPair's P-384 key: Xbox Live's signing scheme requires P-256
// (confirmed in go-xsapi's validateSignatureKey), and this key identifies
// the signed-in device/session across a login, not a single connection.
internal static class XboxLiveProofKey
{
  public static ECDsa Generate() => ECDsa.Create(ECCurve.NamedCurves.nistP256);

  public static ECDsa FromPrivateKeyD(byte[] d)
  {
    var ecdsa = ECDsa.Create();
    ecdsa.ImportParameters(new ECParameters
    {
      Curve = ECCurve.NamedCurves.nistP256,
      D = d,
    });
    return ecdsa;
  }

  public static byte[] ExportPrivateKeyD(ECDsa key) => key.ExportParameters(includePrivateParameters: true).D!;

  // The JSON Web Key representation Xbox Live expects in the "ProofKey"
  // field of device/SISU authorize requests: the public point only, with
  // alg/use set exactly as go-xsapi's internal.ProofKey produces them.
  public static object ToJsonWebKey(ECDsa key)
  {
    ECParameters parameters = key.ExportParameters(includePrivateParameters: false);
    return new
    {
      kty = "EC",
      crv = "P-256",
      x = Base64Url.EncodeToString(parameters.Q.X!),
      y = Base64Url.EncodeToString(parameters.Q.Y!),
      alg = "ES256",
      use = "sig",
    };
  }
}
