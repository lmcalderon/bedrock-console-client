namespace BedrockConsoleClient.Networking.Bedrock;

// Separate from RakNet's ConnectionState on purpose. RakNet's enum belongs to
// the transport layer and correctly stops meaning anything once Connected;
// this one starts from there. Same "explicit enum + guarded transition"
// pattern as RakNet, for the same reason: a linear sequence where each state
// waits on exactly one packet type, not independent per-state behavior.
public enum BedrockLoginState
{
  NotStarted,
  AwaitingNetworkSettings,
  AwaitingPlayStatusLoginOk,
  AwaitingResourcePacksInfo,
  AwaitingResourcePackStack,
  AwaitingStartGame,

  // Confirmed against PMMP's SpawnResponsePacketHandler: the server does not
  // send a second PlayStatus(PLAYER_SPAWN) before this. It waits for
  // SetLocalPlayerAsInitialized from the client immediately after StartGame,
  // and only then completes the spawn. So this state transitions straight to
  // Spawned; there is no separate "waiting for spawn confirmation" state.
  Spawned,
  Disconnected,
}
