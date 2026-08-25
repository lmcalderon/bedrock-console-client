namespace BedrockConsoleClient.Networking.RakNet;

internal static class RakNetMagic
{
  // Fixed 16-byte sequence present in every offline (unconnected) RakNet message.
  public static ReadOnlySpan<byte> Bytes =>
  [
      0x00, 0xFF, 0xFF, 0x00, 0xFE, 0xFE, 0xFE, 0xFE,
        0xFD, 0xFD, 0xFD, 0xFD, 0x12, 0x34, 0x56, 0x78,
    ];
}
