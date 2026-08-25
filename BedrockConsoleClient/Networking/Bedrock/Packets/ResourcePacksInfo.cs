namespace BedrockConsoleClient.Networking.Bedrock.Packets;

using BedrockConsoleClient.Networking.Bedrock.IO;

// Server -> client only. Only PackCount is used. This milestone assumes a
// fresh default server with none configured (see docs/notes/bedrock-login-design.md
// for the disconnect-if-nonzero fallback). worldTemplateId/Version are read
// past (skipped) rather than interpreted; the UUID is stored as two
// byte-reversed 8-byte halves server-side, irrelevant since we never use it.
internal readonly record struct ResourcePacksInfo(bool MustAccept, ushort PackCount)
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
    ushort packCount = reader.ReadUInt16LE();
    return new ResourcePacksInfo(mustAccept, packCount);
  }
}
