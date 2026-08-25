namespace BedrockConsoleClient.Networking.RakNet.Packets;

using BedrockConsoleClient.Networking.RakNet.IO;

// Client -> server only. Sent as a reliable-ordered Frame, not a raw offline
// message. First packet of the "connected" handshake phase.
internal static class ConnectionRequest
{
  public static byte[] Encode(long clientGuid, long requestTime)
  {
    var buffer = new byte[18];
    buffer[0] = (byte)RakNetMessageId.ConnectionRequest;
    var writer = new RakNetSpanWriter(buffer.AsSpan(1));
    writer.WriteInt64BE(clientGuid);
    writer.WriteInt64BE(requestTime);
    writer.WriteBool(false); // Secure - not implementing RakNet-level security for this milestone.
    return buffer;
  }
}
