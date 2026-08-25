namespace BedrockConsoleClient.Networking.Bedrock.Identity;

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class JwtSigner
{
  public static string Sign(object header, object payload, ECDsa key)
  {
    string headerB64 = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header));
    string payloadB64 = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
    string signingInput = $"{headerB64}.{payloadB64}";

    byte[] signature = key.SignData(
        Encoding.UTF8.GetBytes(signingInput),
        HashAlgorithmName.SHA384,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    string signatureB64 = Base64Url.EncodeToString(signature);

    return $"{signingInput}.{signatureB64}";
  }

  // Splits a compact JWS into its parts without verifying the signature.
  // Callers verify separately once they've extracted the signer's public key
  // from the header (e.g. its x5u claim).
  public static (JsonElement Header, JsonElement Payload, byte[] Signature, string SigningInput) Decode(string jws)
  {
    string[] parts = jws.Split('.');
    if (parts.Length != 3)
    {
      throw new FormatException("Expected a 3-part compact JWS.");
    }

    var header = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0])).RootElement.Clone();
    var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1])).RootElement.Clone();
    byte[] signature = Base64Url.DecodeFromChars(parts[2]);
    string signingInput = $"{parts[0]}.{parts[1]}";
    return (header, payload, signature, signingInput);
  }

  public static bool Verify(string signingInput, byte[] signature, ECDsa key) =>
      key.VerifyData(
          Encoding.UTF8.GetBytes(signingInput),
          signature,
          HashAlgorithmName.SHA384,
          DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

  // x5u/cpk claims carry standard Base64 (not Base64Url) DER SubjectPublicKeyInfo,
  // a different encoding than the JWS's own three dot-separated segments.
  public static ECDsa ImportPublicKeyFromDerBase64(string base64Der)
  {
    byte[] der = Convert.FromBase64String(base64Der);
    var ecdsa = ECDsa.Create();
    ecdsa.ImportSubjectPublicKeyInfo(der, out _);
    return ecdsa;
  }
}
