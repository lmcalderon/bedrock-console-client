namespace BedrockConsoleClient.Networking.RakNet;

// RakNet ID assignments are a common source of from-scratch implementation
// bugs; these match github.com/sandertv/go-raknet (internal/message/id.go).
// See docs/notes/raknet-design.md.
internal enum RakNetMessageId : byte
{
  ConnectedPing = 0x00,
  UnconnectedPing = 0x01,
  ConnectedPong = 0x03,
  OpenConnectionRequest1 = 0x05,
  OpenConnectionReply1 = 0x06,
  OpenConnectionRequest2 = 0x07,
  OpenConnectionReply2 = 0x08,
  ConnectionRequest = 0x09,
  ConnectionRequestAccepted = 0x10,
  NewIncomingConnection = 0x13,
  DisconnectNotification = 0x15,
  IncompatibleProtocolVersion = 0x19,
  UnconnectedPong = 0x1C,
}
