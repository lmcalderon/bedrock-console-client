---
name: bedrock-integration-testing
description: >-
  Use when proving BedrockConsoleClient behavior on a real local Bedrock
  server, not just reasoning about whether the RakNet handshake or login
  sequence should work. Covers the discipline for claiming a milestone is
  actually integration tested rather than just built. Local test server
  (official Bedrock Dedicated Server via Docker) is set up - see "Local test
  server" below.
metadata:
  category: discipline
  triggers:
    - integration test
    - real server
    - local server
    - offline mode
    - bedrock dedicated server
    - bds
    - raknet
    - handshake
    - login
    - idle
    - milestone 1
    - milestone 2
    - xbox live
    - microsoft sign-in
---

# Bedrock Integration Testing

Milestones 1 and 2 were previously implemented and integration tested against an earlier local test server (RakNet transport, the full Bedrock login sequence including encryption, and Xbox Live sign-in — see [docs/notes/raknet-design.md](../../docs/notes/raknet-design.md), [docs/notes/bedrock-login-design.md](../../docs/notes/bedrock-login-design.md), and [docs/notes/bedrock-xbox-live-auth-design.md](../../docs/notes/bedrock-xbox-live-auth-design.md) for what those runs found). That server has since been retired as this project's test target — see "Local test server" below — so those results should be treated as historical, not as standing proof against the server software this client actually intends to talk to. Re-verification against a real Bedrock Dedicated Server (BDS) is in progress: RakNet transport, `NetworkSettings` negotiation, and the `Login` packet (identity chain + `ClientData`) are now all confirmed correct against real BDS (`ClientData.DeviceOS` had to be `8`, not `7` - see `bedrock-login-design.md` for how this was found). The Xbox Live path now reaches `ResourcePacksInfo` before hitting an unrelated decode bug; the self-signed path gets past `Login` but is disconnected (reason `143`, not yet identified) before resource-pack negotiation.

Modeled on Minecraft-Console-Client's `mcc-integration-testing` skill, which enforces the same discipline for MCC's Java Edition testing. This is the Bedrock-side, project-specific equivalent — not a copy, since the server, protocol, and harness are all different.

**Both Milestone 1 (self-signed) and Milestone 2 (Xbox Live) are now confirmed end-to-end against real BDS**: full login through the real client-side spawn handshake to `Spawned`, with the server logging a real join and `Player Spawned: <username>`/`<gamertag>`, then indefinite stable idle. See [docs/notes/bedrock-login-design.md](../../docs/notes/bedrock-login-design.md) for the three real bugs this found and fixed (`ClientData.ThirdPartyName` needing to equal the username, a premature `SetLocalPlayerAsInitialized` that never actually completed the spawn server-side, and two packet-decode/encode bugs in `ResourcePacksInfo`/`ResourcePackClientResponse`) and how gophertunnel was used as the debugging oracle to find them - not by reasoning about the spec, but by running it against this same container and diffing real bytes.

## Iron Law

Only say the client was integration tested when it ran against a real local server and the claim is backed by real client output plus real server logs.

These do **not** count as integration tested:

- static reasoning about the RakNet spec or packet format
- build success
- a UDP socket that opens but never completes the RakNet connected handshake
- claiming indefinite idle survival without accounting for the server's own login-completion timeout, if it has one (an earlier test server had one — see [docs/notes/raknet-design.md](../../docs/notes/raknet-design.md) for what was found; BDS's behavior here is unconfirmed). A RakNet-only client reaching `Connected` and proving one keep-alive round trip is real progress, not "stays up forever."

If there is no server running, say so and report the result as unexecuted or inferred, not integration tested.

## Local test server

**Status: official Bedrock Dedicated Server (BDS), run via Docker.** BDS itself is Windows/Linux only, so on macOS it runs in a Colima-managed Docker container rather than natively.

**The one real gotcha**: Colima's default port forwarder is SSH-based and silently drops all UDP - Bedrock/RakNet needs UDP, so Colima must run with `--port-forwarder grpc`:

```
colima stop
colima start --port-forwarder grpc
```

If UDP ever stops working after a reboot/restart, that's the first thing to check (`colima status`, then restart with the flag above if needed).

Container setup (image: `itzg/minecraft-bedrock-server`, an actively maintained wrapper around the real `bedrock_server` binary):

```
docker run -d \
  --name bds-test-server \
  -e EULA=TRUE \
  -e ONLINE_MODE=false \
  -e ALLOW_LIST=false \
  -e SERVER_PORT=19132 \
  -e SERVER_PORT_V6=19133 \
  -p 19133:19132/udp \
  -v <local-data-dir>:/data \
  itzg/minecraft-bedrock-server
```

`ONLINE_MODE=false` is what enables offline-mode/self-signed testing (`server.properties`' `online-mode=false` - note real BDS clients still always use full Xbox Live auth for a "remote"/externally-added server regardless of this setting; it only affects what the *server* requires). The container publishes UDP `19133` on the host, mapped to the server's internal `19132`; `BedrockConsoleClient.ini`'s `ServerAddress=127.0.0.1:19133` (or the host's LAN IP, to let another device on the network connect too) targets that.

To restart the container after editing `server.properties` directly (`docker exec bds-test-server sh -c '...'`), use `docker restart bds-test-server` - the entrypoint re-reads the file on its next start.

## Test modes

The four modes below describe *what* each test should prove. Log-line examples below are illustrative, not exact strings from any specific server - check actual output against real BDS.

### 1. RakNet handshake smoke test

Pass criteria — all of the following, from **both** sides:

- Client log shows `[state]` transitioning `OfflineHandshake1 → OfflineHandshake2 → ConnectedHandshake → Connected`, in order, with no gaps.
- Client log shows at least one `[ping] round trip <N> ms` line while `Connected`.
- Server console/log shows a matching session-opened line at the same wall-clock time (compare timestamps — client logs `HH:mm:ss.fff`).
- On Ctrl+C: client logs `Disconnecting...` then `Shut down.` and exits with code 0; server logs the session closing without an error.

Confirmed against real BDS: the handshake reaches `Connected` reliably. Last full timing/round-trip verification (now historical, against an earlier test server): handshake completes in ~70-90ms on loopback; 3 keep-alive round trips at 5-10ms observed before that server's login timeout closed the session (expected, since no Login packet was sent in this mode).

### 2. Server-initiated disconnect handling

Run the client and do **not** press Ctrl+C. Pass criteria:

- Client log shows `[state] Disconnected` unprompted.
- Client log then shows `Disconnecting...` / `Shut down.` and the process exits with code 0 on its own — it must not hang waiting for Ctrl+C after the server has already closed the session.
- Server log shows the session closing at essentially the same timestamp as the client's `[state] Disconnected` line.

This test caught a real bug during implementation: the client originally had no way to notice a server-initiated disconnect and hung until externally killed. Fixed in `Program.cs` by canceling the idle loop's token when `ConnectionState.Disconnected` fires. (Found against an earlier test server's ~10s login timeout specifically; BDS may not reproduce the same trigger, but the regression this guards against is server-agnostic.)

### 3. Full Bedrock login and indefinite idle

Encryption left on (`enable-encryption`-equivalent) — this should be the real test, not a weakened one. Pass criteria, from **both** sides:

- Client log shows `[login]` transitioning `AwaitingNetworkSettings → AwaitingPlayStatusLoginOk → AwaitingResourcePacksInfo → AwaitingResourcePackStack → AwaitingStartGame → Spawned`, in order, no gaps, no `[error]` lines.
- Server log shows a real join (username + entity id), not just a session-opened line.
- Client continues logging `[ping] round trip <N> ms` indefinitely after `Spawned` with no disconnect — the actual Milestone 1 bar ("staying connected and idling without being dropped").
- On Ctrl+C: clean disconnect on both sides, same as test mode 1.

**Confirmed against real BDS, full login through spawn**: `RequestNetworkSettings` → `Login` → encryption → `PlayStatus` → `ResourcePacksInfo` → `ResourcePackStack` → `StartGame` → `ItemRegistry` → `RequestChunkRadius` → `ChunkRadiusUpdated`/`PlayStatus(PlayerSpawn)` → `SetLocalPlayerAsInitialized` → `Spawned`, then indefinite idle with keep-alive pings and no disconnect. Server log shows `Player connected: <username>` then `Player Spawned: <username>` at matching timestamps. See [docs/notes/bedrock-login-design.md](../../docs/notes/bedrock-login-design.md) for the bugs found getting here.

Previously verified end-to-end against an earlier test server that has since been retired: full login (`RequestNetworkSettings` → `Spawned`) completed in ~300ms on loopback. That run caught four real bugs during implementation — a missing JWT claim, a wrong field name plus ~10 missing required fields in the client-data JWT, a missing required `aud` claim, and (the significant one) an AES-CTR keystream that wasn't a continuous stream across encrypt/decrypt calls, which silently corrupted every packet after the first. That result was historical only until the real-BDS verification above superseded it.

### 4. Milestone 2: Xbox Live sign-in

Requires the local server to actually enforce Xbox Live authentication. Set `Mode=Microsoft` under `[Auth]` in `BedrockConsoleClient.ini`.

First run, no cached token — pass criteria, from **both** sides:

- Client logs `[auth] Sign in to your Microsoft account at <url> using the code <code>.`, a real device-code prompt from `login.live.com`.
- After completing sign-in in a browser (a human, once, per machine — the one step that can't run unattended), client logs `[auth] Signed in to Xbox Live as <real gamertag>.`, then proceeds through `[login]` states to `Spawned`.
- Server log shows a real join with a real gamertag, not the self-signed placeholder username.

Second run, cached token present (`BedrockConsoleClient.msa-cache` next to the executable) — pass criteria:

- No device-code prompt; client logs `[auth] Refreshing cached Microsoft sign-in...` then `Signed in to Xbox Live as <gamertag>.` and reaches `Spawned` non-interactively.

Regression check after any change to the identity-provider seam: re-run test mode 3 above (`Mode=SelfSigned`) and confirm it still reaches `Spawned` unmodified — `BedrockSession.LoginAsync`'s signature changed to accept an `IIdentityChainProvider`, so this isn't automatically safe to assume.

**Currently failing against real BDS, past `Login`**: with the `ClientData.DeviceOS` fix (see test mode 3 above), this path now reaches `Login` → encryption handshake → `PlayStatus` → `ResourcePacksInfo` successfully, then fails on an unrelated decode bug in this project's own `ResourcePacksInfo.Decode` (out-of-range length read). Not yet fixed.

Last verified end-to-end (now historical, against an earlier test server that has since been retired): this test caught two real bugs during implementation — PlayFab's relying party missing from the generic NSAL endpoint table (needed the title-specific, authenticated table instead) and a response-envelope key mismatch between PlayFab's own API convention (`data`) and the franchise services' convention (`result`), which the first draft applied to both. See [docs/notes/bedrock-xbox-live-auth-design.md](../../docs/notes/bedrock-xbox-live-auth-design.md) for details.

Revert `Mode` to `SelfSigned` after this test, matching this skill's default test target.
