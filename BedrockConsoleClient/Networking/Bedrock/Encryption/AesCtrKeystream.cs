namespace BedrockConsoleClient.Networking.Bedrock.Encryption;

using System.Buffers.Binary;
using System.Security.Cryptography;

// .NET's Aes class has no native CTR mode. This encrypts successive 16-byte
// counter blocks with plain ECB (no padding) as the block permutation and
// XORs the keystream into the payload. CTR is its own inverse, so the same
// routine serves both encrypt and decrypt. state is a genuine continuous
// byte-stream position (not just a block counter): PMMP's underlying cipher
// API doesn't align to block boundaries per call, so a payload that doesn't
// end on a 16-byte boundary must carry its leftover keystream bytes into the
// next Transform call rather than discarding them.
internal static class AesCtrKeystream
{
  public static void Transform(ICryptoTransform ecbEncryptor, ReadOnlySpan<byte> nonce12, CtrKeystreamState state, Span<byte> data)
  {
    var counterBlock = new byte[16];
    nonce12[..12].CopyTo(counterBlock);

    for (int i = 0; i < data.Length; i++)
    {
      if (state.Offset >= 16)
      {
        BinaryPrimitives.WriteUInt32BigEndian(counterBlock.AsSpan(12), state.BlockCounter);
        ecbEncryptor.TransformBlock(counterBlock, 0, 16, state.CurrentBlock, 0);
        state.BlockCounter++;
        state.Offset = 0;
      }

      data[i] ^= state.CurrentBlock[state.Offset];
      state.Offset++;
    }
  }
}
