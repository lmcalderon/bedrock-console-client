namespace BedrockConsoleClient.Auth.XboxLive;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

// Ports go-xsapi's xal/internal/signature.go. The exact byte layout - field
// order, the 0x00 separators between hashed fields, and the raw r||s P-256
// signature format Xbox Live expects instead of DER - is load-bearing; a
// request with a malformed Signature header is simply rejected, not
// partially accepted. Confirmed against go-xsapi source, not guessed from
// documentation. See docs/notes/bedrock-xbox-live-auth-design.md.
internal static class XboxLiveRequestSigner
{
  // Windows FILETIME's epoch (1601-01-01) versus Unix's, in 100ns ticks.
  // https://learn.microsoft.com/en-us/windows/win32/sysinfo/file-times
  private const long WindowsEpochOffsetTicks = 116444736000000000L;

  public static void Sign(HttpRequestMessage request, byte[] body, ECDsa proofKey, XboxLiveSignaturePolicy policy)
  {
    byte[] signature = Generate(request, body, proofKey, policy, DateTimeOffset.UtcNow);
    request.Headers.Remove("Signature");
    request.Headers.TryAddWithoutValidation("Signature", Convert.ToBase64String(signature));
  }

  private static byte[] Generate(HttpRequestMessage request, byte[] body, ECDsa proofKey, XboxLiveSignaturePolicy policy, DateTimeOffset timestamp)
  {
    long windowsTimestamp = (timestamp.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) + WindowsEpochOffsetTicks;

    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    Span<byte> prefix = stackalloc byte[14];
    BinaryPrimitives.WriteUInt32BigEndian(prefix[..4], policy.Version);
    prefix[4] = 0;
    BinaryPrimitives.WriteInt64BigEndian(prefix[5..13], windowsTimestamp);
    prefix[13] = 0;
    hash.AppendData(prefix);

    hash.AppendData(Encoding.ASCII.GetBytes(request.Method.Method));
    hash.AppendData([0]);

    string path = request.RequestUri!.AbsolutePath + request.RequestUri.Query;
    hash.AppendData(Encoding.UTF8.GetBytes(path));
    hash.AppendData([0]);

    foreach (string headerName in ExtraHeadersAfterAuthorization(policy))
    {
      string value = request.Headers.TryGetValues(headerName, out var values) ? values.First() : string.Empty;
      hash.AppendData(Encoding.UTF8.GetBytes(value));
      hash.AppendData([0]);
    }

    int bodyLength = policy.MaxBodyBytes == 0 ? body.Length : Math.Min(policy.MaxBodyBytes, body.Length);
    hash.AppendData(body.AsSpan(0, bodyLength));
    hash.AppendData([0]);

    byte[] digest = hash.GetHashAndReset();

    // P-256 signature as raw, zero-padded r||s (64 bytes) - not the DER
    // encoding ECDsa.SignData/SignHash produce by default.
    byte[] rawSignature = proofKey.SignHash(digest, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    byte[] output = new byte[4 + 8 + rawSignature.Length];
    BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(0, 4), policy.Version);
    BinaryPrimitives.WriteInt64BigEndian(output.AsSpan(4, 8), windowsTimestamp);
    rawSignature.CopyTo(output.AsSpan(12));
    return output;
  }

  private static IEnumerable<string> ExtraHeadersAfterAuthorization(XboxLiveSignaturePolicy policy)
  {
    yield return "Authorization";
    foreach (string header in policy.ExtraHeaders)
    {
      yield return header;
    }
  }
}
