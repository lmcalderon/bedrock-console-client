namespace BedrockConsoleClient.Networking.Bedrock.Packets;

using BedrockConsoleClient.Networking.Bedrock.IO;

internal enum PacketViolationSeverity
{
  Warning = 0,
  FinalWarning = 1,
  TerminatingConnection = 2,
}

// Server -> client only. Sent when the server couldn't parse a packet this
// client sent (e.g. Login) - reports which packet ID and why, immediately
// before the server disconnects for anything at TerminatingConnection
// severity.
internal readonly record struct PacketViolationWarning(int Type, PacketViolationSeverity Severity, int PacketId, string Message)
{
  public static PacketViolationWarning Decode(ReadOnlySpan<byte> payload)
  {
    var reader = new BedrockVarIntReader(payload);
    int type = reader.ReadVarInt32();
    var severity = (PacketViolationSeverity)reader.ReadVarInt32();
    int packetId = reader.ReadVarInt32();
    string message = reader.ReadString();
    return new PacketViolationWarning(type, severity, packetId, message);
  }
}
