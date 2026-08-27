namespace BedrockConsoleClient.Networking.Bedrock.Encryption;

using System.Security.Cryptography;

internal static class HandshakeKeyExchange
{
  // AES key = SHA-256(salt(16 bytes) || raw ECDH shared secret(48 bytes for
  // P-384)). One pass, no HKDF, confirmed identically across gophertunnel
  // and bedrock-protocol source. Uses DeriveRawSecretAgreement
  // specifically: the BCL's other Derive* helpers apply KDF constructions
  // that don't match this salt-then-secret ordering.
  public static byte[] DeriveKey(ECDiffieHellman clientKey, ECDiffieHellman serverKey, ReadOnlySpan<byte> salt)
  {
    byte[] sharedSecret = clientKey.DeriveRawSecretAgreement(serverKey.PublicKey);
    var combined = new byte[salt.Length + sharedSecret.Length];
    salt.CopyTo(combined);
    sharedSecret.CopyTo(combined.AsSpan(salt.Length));
    return SHA256.HashData(combined);
  }
}
