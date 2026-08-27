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

  // Sending SetLocalPlayerAsInitialized immediately after StartGame (an
  // earlier attempt at this, based on an earlier/retired test server) leaves
  // real BDS never logging the join as "Player Spawned" server-side, even
  // though the client keeps idling without being disconnected - confirmed by
  // diffing against gophertunnel's real, successful client-side spawn
  // sequence (minecraft/conn.go): StartGame -> ItemRegistry -> client sends
  // RequestChunkRadius -> both ChunkRadiusUpdated and PlayStatus(PlayerSpawn)
  // must arrive (order not guaranteed) -> only then does the client send
  // SetLocalPlayerAsInitialized. AwaitingItemRegistry/AwaitingSpawnConfirmation
  // model those two extra waits; BedrockSession tracks the "both arrived" half
  // of AwaitingSpawnConfirmation with two booleans rather than further states,
  // since the two packets are independent, not sequential.
  AwaitingItemRegistry,
  AwaitingSpawnConfirmation,

  Spawned,
  Disconnected,
}
