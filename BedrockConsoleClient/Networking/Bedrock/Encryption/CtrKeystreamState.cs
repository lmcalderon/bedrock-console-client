namespace BedrockConsoleClient.Networking.Bedrock.Encryption;

// Persistent per-direction CTR keystream position. PMMP's underlying cipher
// API (Crypto\Cipher::encryptUpdate/decryptUpdate) is a continuous byte
// stream, not block-aligned per call. A payload that doesn't end on a
// 16-byte boundary leaves keystream bytes that must carry into the next
// call, not get discarded. Offset starts at 16 ("no bytes available yet")
// so the first Transform call generates the first block on demand.
internal sealed class CtrKeystreamState(uint startingBlockCounter)
{
  public uint BlockCounter { get; set; } = startingBlockCounter;

  public byte[] CurrentBlock { get; } = new byte[16];

  public int Offset { get; set; } = 16;
}
