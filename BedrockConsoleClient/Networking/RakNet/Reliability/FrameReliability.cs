namespace BedrockConsoleClient.Networking.RakNet.Reliability;

// Only the 5 base reliability types are needed. go-raknet's actively maintained
// implementation doesn't use the legacy "WithAckReceipt" variants some older
// RakNet docs describe, and this client has no use for delivery receipts.
internal enum FrameReliability : byte
{
  Unreliable = 0,
  UnreliableSequenced = 1,
  Reliable = 2,
  ReliableOrdered = 3,
  ReliableSequenced = 4,
}
