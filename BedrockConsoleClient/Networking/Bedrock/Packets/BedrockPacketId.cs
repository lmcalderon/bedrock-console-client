namespace BedrockConsoleClient.Networking.Bedrock.Packets;

// Values verified against reference protocol source, not guessed from
// general Bedrock protocol familiarity. Packet IDs have shifted across
// protocol versions historically.
internal enum BedrockPacketId : uint
{
  Login = 0x01,
  PlayStatus = 0x02,
  ServerToClientHandshake = 0x03,
  ClientToServerHandshake = 0x04,
  Disconnect = 0x05,
  ResourcePacksInfo = 0x06,
  ResourcePackStack = 0x07,
  ResourcePackClientResponse = 0x08,
  StartGame = 0x0B,
  RequestChunkRadius = 0x45,
  ChunkRadiusUpdated = 0x46,
  SetLocalPlayerAsInitialized = 0x71,
  NetworkSettings = 0x8F,
  PacketViolationWarning = 0x9C,
  ItemRegistry = 0xA2,
  RequestNetworkSettings = 0xC1,
}
