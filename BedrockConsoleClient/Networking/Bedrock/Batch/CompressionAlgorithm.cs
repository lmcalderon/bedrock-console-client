namespace BedrockConsoleClient.Networking.Bedrock.Batch;

internal enum CompressionAlgorithm : byte
{
  Zlib = 0,
  Snappy = 1,
  None = 255,
}
