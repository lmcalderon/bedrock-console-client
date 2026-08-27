namespace BedrockConsoleClient.Networking.Bedrock.Encryption;

using System.Buffers.Binary;
using System.Security.Cryptography;

// Wraps one negotiated AES-256 key for the lifetime of a Bedrock connection.
// Real AES-256-CTR keystream, no authenticated encryption despite the
// protocol calling this "GCM"; integrity instead comes from an 8-byte
// SHA-256-based trailer, a construction confirmed identically across
// gophertunnel/bedrock-protocol source. Send/receive counters are
// independent.
internal sealed class BedrockEncryptionContext : IDisposable
{
  private readonly byte[] _key;
  private readonly Aes _aes;
  private readonly ICryptoTransform _ecbEncryptor;

  // Mimics the "block 1 reserved for the GCM auth tag" convention this
  // construction imitates, so keystreams start at block counter 2.
  private readonly CtrKeystreamState _sendKeystream = new(startingBlockCounter: 2);
  private readonly CtrKeystreamState _receiveKeystream = new(startingBlockCounter: 2);
  private ulong _sendChecksumCounter;
  private ulong _receiveChecksumCounter;

  public BedrockEncryptionContext(byte[] key)
  {
    _key = key;
    _aes = Aes.Create();
    _aes.Key = key;
    _aes.Mode = CipherMode.ECB;
    _aes.Padding = PaddingMode.None;
    _ecbEncryptor = _aes.CreateEncryptor();
  }

  // Appends the checksum trailer, then CTR-encrypts (batch body + trailer)
  // as one unit. Encryption applies once per outgoing batch, not per packet.
  public byte[] Encrypt(ReadOnlySpan<byte> plaintext)
  {
    var buffer = new byte[plaintext.Length + 8];
    plaintext.CopyTo(buffer);
    ComputeChecksum(_sendChecksumCounter, plaintext).CopyTo(buffer.AsSpan(plaintext.Length));
    _sendChecksumCounter++;

    AesCtrKeystream.Transform(_ecbEncryptor, _key, _sendKeystream, buffer);
    return buffer;
  }

  public byte[] Decrypt(ReadOnlySpan<byte> ciphertext)
  {
    if (ciphertext.Length < 8)
    {
      throw new BedrockDecryptionException("Encrypted batch shorter than the checksum trailer.");
    }

    byte[] buffer = ciphertext.ToArray();
    AesCtrKeystream.Transform(_ecbEncryptor, _key, _receiveKeystream, buffer);

    var plaintext = buffer.AsSpan(0, buffer.Length - 8);
    var receivedChecksum = buffer.AsSpan(buffer.Length - 8);
    byte[] expectedChecksum = ComputeChecksum(_receiveChecksumCounter, plaintext);
    _receiveChecksumCounter++;

    if (!receivedChecksum.SequenceEqual(expectedChecksum))
    {
      throw new BedrockDecryptionException("Checksum mismatch on decrypted batch - possible desync or wrong key.");
    }

    return plaintext.ToArray();
  }

  // checksum = SHA256(LE_UInt64(counter) || plaintext || key)[0:8].
  private byte[] ComputeChecksum(ulong counter, ReadOnlySpan<byte> plaintext)
  {
    Span<byte> counterBytes = stackalloc byte[8];
    BinaryPrimitives.WriteUInt64LittleEndian(counterBytes, counter);

    using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    sha256.AppendData(counterBytes);
    sha256.AppendData(plaintext);
    sha256.AppendData(_key);
    return sha256.GetHashAndReset()[..8];
  }

  public void Dispose()
  {
    _ecbEncryptor.Dispose();
    _aes.Dispose();
  }
}
