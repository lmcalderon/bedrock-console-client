namespace BedrockConsoleClient.Networking.Bedrock.Packets;

using BedrockConsoleClient.Networking.Bedrock.IO;

// Server -> client only. Sent when the server ends the session before or
// during login/play (kicked, server full, not on an allow-list, ...).
// Message/FilteredMessage are absent (null) when the reason is meant to be
// shown to the player with no extra text - e.g. some "server full" replies.
internal readonly record struct Disconnect(int Reason, string? Message, string? FilteredMessage)
{
  public static Disconnect Decode(ReadOnlySpan<byte> payload)
  {
    var reader = new BedrockVarIntReader(payload);
    int reason = reader.ReadVarInt32();
    uint hasMessage = reader.ReadVarUInt32(); // 0 = message follows, 1 = none
    string? message = null;
    string? filteredMessage = null;
    if (hasMessage == 0)
    {
      message = reader.ReadString();
      filteredMessage = reader.ReadString();
    }

    return new Disconnect(reason, message, filteredMessage);
  }
}
