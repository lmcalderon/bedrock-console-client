namespace BedrockConsoleClient.Networking.Bedrock.Batch;

using System.IO.Compression;
using BedrockConsoleClient.Networking.Bedrock.IO;

// Bedrock game packets never travel alone: one or more get length-prefixed,
// concatenated, and (once negotiated) compressed into a single "batch". The
// caller owns the leading 0xFE marker byte and any encryption wrapping this
// codec's output. This type only handles compression and packet framing.
// Every packet here is length-delimited before any packet-specific decoding
// happens, so an unparsed/partially-parsed packet can never desync the batch.
internal static class PacketBatchCodec
{
  // Before compression is negotiated (RequestNetworkSettings / NetworkSettings),
  // there is no compression-algorithm byte and no compression at all.
  public static byte[] EncodeUnnegotiated(IReadOnlyList<byte[]> packets) => ConcatenateLengthPrefixed(packets);

  public static List<byte[]> DecodeUnnegotiated(ReadOnlySpan<byte> body) => SplitLengthPrefixed(body);

  // Returns [algorithmByte, ...payload]: this whole thing is what gets
  // encrypted as one unit once encryption is active.
  public static byte[] Encode(IReadOnlyList<byte[]> packets, CompressionAlgorithm algorithm, int compressionThreshold)
  {
    byte[] body = ConcatenateLengthPrefixed(packets);
    byte[] payload;
    CompressionAlgorithm chosen;
    if (algorithm == CompressionAlgorithm.Zlib && body.Length >= compressionThreshold)
    {
      payload = DeflateCompress(body);
      chosen = CompressionAlgorithm.Zlib;
    }
    else if (algorithm == CompressionAlgorithm.Snappy && body.Length >= compressionThreshold)
    {
      throw new NotSupportedException("Snappy compression is not implemented.");
    }
    else
    {
      payload = body;
      chosen = CompressionAlgorithm.None;
    }

    var buffer = new byte[1 + payload.Length];
    buffer[0] = (byte)chosen;
    payload.CopyTo(buffer.AsSpan(1));
    return buffer;
  }

  // Expects the same [algorithmByte, ...payload] shape Encode produces.
  public static List<byte[]> Decode(ReadOnlySpan<byte> algorithmAndPayload)
  {
    var algorithm = (CompressionAlgorithm)algorithmAndPayload[0];
    var payload = algorithmAndPayload[1..];
    byte[] body = algorithm switch
    {
      CompressionAlgorithm.Zlib => DeflateDecompress(payload),
      CompressionAlgorithm.None => payload.ToArray(),
      CompressionAlgorithm.Snappy => throw new NotSupportedException("Snappy compression is not implemented."),
      _ => throw new NotSupportedException($"Unknown compression algorithm {(byte)algorithm}."),
    };
    return SplitLengthPrefixed(body);
  }

  private static byte[] ConcatenateLengthPrefixed(IReadOnlyList<byte[]> packets)
  {
    int totalLength = 0;
    foreach (byte[] packet in packets)
    {
      totalLength += VarUInt32Size((uint)packet.Length) + packet.Length;
    }

    var buffer = new byte[totalLength];
    var writer = new BedrockVarIntWriter(buffer);
    foreach (byte[] packet in packets)
    {
      writer.WriteVarUInt32((uint)packet.Length);
      writer.WriteBytes(packet);
    }

    return buffer;
  }

  private static List<byte[]> SplitLengthPrefixed(ReadOnlySpan<byte> body)
  {
    var reader = new BedrockVarIntReader(body);
    var packets = new List<byte[]>();
    while (reader.Remaining > 0)
    {
      int length = (int)reader.ReadVarUInt32();
      packets.Add(reader.ReadBytes(length).ToArray());
    }

    return packets;
  }

  private static int VarUInt32Size(uint value)
  {
    int size = 1;
    while ((value & ~0x7Fu) != 0)
    {
      value >>= 7;
      size++;
    }

    return size;
  }

  // Raw deflate, not zlib-wrapped, despite the enum name "Zlib". Bedrock
  // uses Node's/OpenSSL's raw-deflate APIs with no 2-byte header/Adler32
  // trailer. DeflateStream matches; ZLibStream would add a wrapper the
  // real protocol doesn't send.
  private static byte[] DeflateCompress(byte[] data)
  {
    using var output = new MemoryStream();
    using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
    {
      deflate.Write(data);
    }

    return output.ToArray();
  }

  private static byte[] DeflateDecompress(ReadOnlySpan<byte> data)
  {
    using var input = new MemoryStream(data.ToArray());
    using var deflate = new DeflateStream(input, CompressionMode.Decompress);
    using var output = new MemoryStream();
    deflate.CopyTo(output);
    return output.ToArray();
  }
}
