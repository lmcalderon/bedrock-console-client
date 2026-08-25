namespace BedrockConsoleClient.Networking.Bedrock.Packets;

using BedrockConsoleClient.Networking.Bedrock.IO;

// Server -> client only. Only actorRuntimeId is needed (for
// SetLocalPlayerAsInitialized). Every other field (block palette, game
// rules, dozens more) is safe to leave unparsed: the batch's own length
// framing means an under-read here can never desync the packet stream.
internal static class StartGame
{
  public static ulong DecodeActorRuntimeId(ReadOnlySpan<byte> payload)
  {
    var reader = new BedrockVarIntReader(payload);
    reader.ReadVarInt64(); // actorUniqueId - zigzag signed, discarded
    return reader.ReadVarUInt64(); // actorRuntimeId - unsigned, this is what we need
  }
}
