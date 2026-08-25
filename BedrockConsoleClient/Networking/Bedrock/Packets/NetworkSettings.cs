namespace BedrockConsoleClient.Networking.Bedrock.Packets;

using BedrockConsoleClient.Networking.Bedrock.Batch;
using BedrockConsoleClient.Networking.Bedrock.IO;

// Server -> client only. Also sent uncompressed/unencrypted. This reply
// necessarily predates the negotiation it delivers. Full payload has
// throttling fields after these two; this client has no use for them.
internal readonly record struct NetworkSettings(ushort CompressionThreshold, CompressionAlgorithm CompressionAlgorithm)
{
  public static NetworkSettings Decode(ReadOnlySpan<byte> payload)
  {
    var reader = new BedrockVarIntReader(payload);
    ushort threshold = reader.ReadUInt16LE();
    ushort algorithm = reader.ReadUInt16LE();
    return new NetworkSettings(threshold, (CompressionAlgorithm)algorithm);
  }
}
