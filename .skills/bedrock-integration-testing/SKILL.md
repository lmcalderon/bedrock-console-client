---
name: bedrock-integration-testing
description: >-
  Use when proving BedrockConsoleClient behavior on a real local Bedrock
  server, not just reasoning about whether the RakNet handshake or login
  sequence should work. Covers spinning up a local offline-mode PocketMine-MP
  server on macOS and the discipline for claiming a milestone is actually
  integration tested rather than just built.
metadata:
  category: discipline
  triggers:
    - integration test
    - real server
    - local server
    - offline mode
    - pocketmine
    - raknet
    - handshake
    - login
    - idle
    - milestone 1
---

# Bedrock Integration Testing

Milestone 1 is fully implemented and integration tested as of this writing: RakNet transport (see [docs/notes/raknet-design.md](../../docs/notes/raknet-design.md)) and the Bedrock login sequence, including encryption (see [docs/notes/bedrock-login-design.md](../../docs/notes/bedrock-login-design.md)). The client reaches `Spawned` and idles indefinitely, past the 10-second RakNet-only login timeout that motivated the login work in the first place.

Modeled on Minecraft-Console-Client's `mcc-integration-testing` skill, which enforces the same discipline for MCC's Java Edition testing. This is the Bedrock-side, project-specific equivalent — not a copy, since the server, protocol, and harness are all different.

## Iron Law

Only say the client was integration tested when it ran against a real local server and the claim is backed by real client output plus real server logs (PocketMine-MP's console output / `server.log`).

These do **not** count as integration tested:

- static reasoning about the RakNet spec or packet format
- build success
- a UDP socket that opens but never completes the RakNet connected handshake
- claiming indefinite idle survival without accounting for PocketMine-MP's login-completion timeout (confirmed: it closes a RakNet-only session — no Bedrock `Login` packet ever sent — exactly 10 seconds after `Session opened`, regardless of healthy `ConnectedPing`/`ConnectedPong` traffic; see docs/notes/raknet-design.md). A RakNet-only client reaching `Connected` and proving one keep-alive round trip is real progress, not "stays up forever."

If there is no server running, say so and report the result as unexecuted or inferred, not integration tested.

## Local test server (macOS)

Official Mojang Bedrock Dedicated Server is Windows/Linux only. Use **PocketMine-MP** instead — it has an official macOS installer and bundles its own PHP runtime, so no Wine or Docker workaround is needed.

```bash
mkdir -p ~/Minecraft/pmmp-test-server && cd ~/Minecraft/pmmp-test-server
curl -sL https://get.pmmp.io | bash -s -
./start.sh   # first run generates server.properties; Ctrl+C once it's up, then edit the file
```

In the generated `server.properties`:

```
xbox-auth=false
server-port=19132
```

Restart with `./start.sh`. `xbox-auth=false` is what lets the client present a self-signed identity chain instead of a real Microsoft/XBL-signed one — this is what Milestone 1 needs, since Milestone 2 (Xbox Live auth) doesn't exist yet.

**Default target for this project:** `localhost:19132`, offline mode.

Installed and verified on this machine at `~/Minecraft/pmmp-test-server` (outside the repo — it's a local dev tool, not project source). Start it with `./start.sh --no-wizard` to skip the interactive language/license prompts and load `server.properties` as-is; useful in any script that starts it non-interactively.

Reference source for protocol details is also cloned locally at `~/Minecraft/pmmp-reference/` (`RakLib`, `BedrockProtocol`, `PocketMine-MP`) — `grep -r` there instead of one-off `curl`ing individual files from GitHub. This is the ground truth for whatever the local test server actually runs, not just "a" reference implementation.

## Test modes

### 1. RakNet handshake smoke test

```bash
cd ~/Minecraft/pmmp-test-server && ./start.sh --no-wizard &
cd /path/to/bedrock-console-client && dotnet run --project BedrockConsoleClient
```

Pass criteria — all of the following, from **both** sides:

- Client log shows `[state]` transitioning `OfflineHandshake1 → OfflineHandshake2 → ConnectedHandshake → Connected`, in order, with no gaps.
- Client log shows at least one `[ping] round trip <N> ms` line while `Connected`.
- PocketMine-MP's console/log shows a matching `[NetworkSession: <addr>] Session opened` at the same wall-clock time (compare timestamps — both sides log `HH:mm:ss.fff`).
- On Ctrl+C: client logs `Disconnecting...` then `Shut down.` and exits with code 0; PocketMine-MP logs the session closing without an `[ERROR]` line.

Last verified: handshake completes in ~70-90ms on loopback; 3 keep-alive round trips at 5-10ms observed before PMMP's login timeout closed the session (expected — see below).

### 2. Server-initiated disconnect handling

Since PMMP always ends a RakNet-only session by its login timeout (not by the client choosing to disconnect), this doubles as a regression test for that path: run the client and do **not** press Ctrl+C. Pass criteria:

- Client log shows `[state] Disconnected` unprompted, roughly 10s after `Connected`.
- Client log then shows `Disconnecting...` / `Shut down.` and the process exits with code 0 on its own — it must not hang waiting for Ctrl+C after the server has already closed the session.
- PocketMine-MP's log shows `Session closed: Login timeout` at essentially the same timestamp as the client's `[state] Disconnected` line (sub-10ms apart on loopback).

This test caught a real bug during implementation: the client originally had no way to notice a server-initiated disconnect and hung until externally killed. Fixed in `Program.cs` by canceling the idle loop's token when `ConnectionState.Disconnected` fires.

### 3. Full Bedrock login and indefinite idle

```bash
cd ~/Minecraft/pmmp-test-server && ./start.sh --no-wizard &
cd /path/to/bedrock-console-client && dotnet run --project BedrockConsoleClient
```

Default PMMP config (`enable-encryption: true` left on — this is the real test, not a weakened one). Pass criteria, from **both** sides:

- Client log shows `[login]` transitioning `AwaitingNetworkSettings → AwaitingPlayStatusLoginOk → AwaitingResourcePacksInfo → AwaitingResourcePackStack → AwaitingStartGame → Spawned`, in order, no gaps, no `[error]` lines.
- PocketMine-MP's log shows `Player: <username>` followed by `<username>[...] logged in with entity id N at (...)` — a real join, not just `Session opened`.
- Client continues logging `[ping] round trip <N> ms` indefinitely after `Spawned` (verified past 36+ seconds continuous in practice) with no disconnect — the actual Milestone 1 bar ("staying connected and idling without being dropped"), finally achievable now that a real `Login` packet is sent.
- On Ctrl+C: clean disconnect on both sides, same as test mode 1.

Last verified: full login (`RequestNetworkSettings` → `Spawned`) completes in ~300ms on loopback.

This test caught four real bugs during implementation — a missing JWT claim, a wrong field name plus ~10 missing required fields in the client-data JWT, a missing required `aud` claim, and (the significant one) an AES-CTR keystream that wasn't a continuous stream across encrypt/decrypt calls, which silently corrupted every packet after the first. All four were resolved by reading PMMP's actual source (cloned locally at `~/Minecraft/pmmp-reference/`) rather than guessing from reference-client conventions. See [docs/notes/bedrock-login-design.md](../../docs/notes/bedrock-login-design.md) for details.
