namespace BedrockConsoleClient.Networking.Bedrock.Packets;

// Values verified against github.com/pmmp/BedrockProtocol's ProtocolInfo.php
// (the library the local PMMP test server runs), not guessed from general
// Bedrock protocol familiarity. Packet IDs have shifted across protocol
// versions historically.
internal enum BedrockPacketId : uint
{
  Login = 0x01,
  PlayStatus = 0x02,
  ServerToClientHandshake = 0x03,
  ClientToServerHandshake = 0x04,
  ResourcePacksInfo = 0x06,
  ResourcePackStack = 0x07,
  ResourcePackClientResponse = 0x08,
  StartGame = 0x0B,
  SetLocalPlayerAsInitialized = 0x71,
  NetworkSettings = 0x8F,
  RequestNetworkSettings = 0xC1,
}
