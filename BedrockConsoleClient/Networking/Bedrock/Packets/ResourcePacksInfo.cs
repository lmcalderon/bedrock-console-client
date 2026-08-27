namespace BedrockConsoleClient.Networking.Bedrock.Packets;

using BedrockConsoleClient.Networking.Bedrock.IO;

// Server -> client only. Only PackCount is used. This milestone assumes a
// fresh default server with none configured (see docs/notes/bedrock-login-design.md
// for the disconnect-if-nonzero fallback). worldTemplateId/Version are read
// past (skipped) rather than interpreted; the UUID is stored as two
// byte-reversed 8-byte halves server-side, irrelevant since we never use it.
internal readonly record struct ResourcePacksInfo(bool MustAccept, uint PackCount)
{
  public static ResourcePacksInfo Decode(ReadOnlySpan<byte> payload)
  {
    var reader = new BedrockVarIntReader(payload);
    bool mustAccept = reader.ReadBool();
    reader.ReadBool(); // hasAddons
    reader.ReadBool(); // hasScripts
    reader.ReadBool(); // forceDisableVibrantVisuals
    reader.Advance(16); // worldTemplateId (UUID)
    reader.ReadString(); // worldTemplateVersion

    // The texture-pack list is a VarUInt32-prefixed slice, not a fixed
    // uint16 - confirmed from gophertunnel's protocol.Slice (used for this
    // field via ResourcePacksInfo.Marshal), which reads/writes the count
    // with io.Varuint32. Reading it as a fixed 2-byte LE value (an earlier
    // attempt at this) desyncs the reader and throws an out-of-range error
    // on whatever it reads next.
    uint packCount = reader.ReadVarUInt32();
    return new ResourcePacksInfo(mustAccept, packCount);
  }
}
