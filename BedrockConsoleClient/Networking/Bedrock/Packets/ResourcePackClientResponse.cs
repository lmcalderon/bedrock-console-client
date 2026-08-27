namespace BedrockConsoleClient.Networking.Bedrock.Packets;

using BedrockConsoleClient.Networking.Bedrock.IO;

internal enum ResourcePackClientResponseStatus : uint
{
  Refused = 0,
  SendPacks = 1,
  HaveAllPacks = 2,
  Completed = 3,
}

// Client -> server only. The status is sent twice on the wire - once as a
// VarUInt32, once as its string name ("cancel"/"downloading"/
// "downloadingfinished"/"resourcepackstackfinished") - confirmed from
// gophertunnel's ResourcePackClientResponse.Marshal: real BDS's own decoder
// is schema-bound (it names the field type "ResourcePackResponse") and
// rejects a missing/empty string with a PacketViolationWarning ("expects one
// of the following strings... Found: ''"), which is what an earlier
// raw-byte-only encoding of this packet hit. PacksToDownload is never
// written since this client never sends SendPacks.
internal static class ResourcePackClientResponse
{
  private static readonly string[] s_statusNames =
      ["cancel", "downloading", "downloadingfinished", "resourcepackstackfinished"];

  public static byte[] Encode(ResourcePackClientResponseStatus status)
  {
    var buffer = new byte[48];
    var writer = new BedrockVarIntWriter(buffer);
    writer.WriteVarUInt32((uint)BedrockPacketId.ResourcePackClientResponse);
    writer.WriteVarUInt32((uint)status);
    writer.WriteString(s_statusNames[(uint)status]);
    return buffer[..writer.Position];
  }
}
