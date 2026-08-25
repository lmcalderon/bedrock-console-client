namespace BedrockConsoleClient.Networking.RakNet;

public enum ConnectionState
{
  Unconnected,
  OfflineHandshake1,
  OfflineHandshake2,
  ConnectedHandshake,
  Connected,
  Disconnected,
}
