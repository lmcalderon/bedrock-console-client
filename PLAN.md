# Milestones

## Milestone 0 — skeleton (done)

A `net10.0` console app (`BedrockConsoleClient/`) that builds, runs, prints a startup message, and exits cleanly on Ctrl+C. No networking, no dependencies beyond the .NET SDK. Purpose: prove the repo/toolchain scaffolding works before any protocol code is written.

## Milestone 1 — offline-mode local connect (done)

Implement a minimal RakNet client (connection handshake, reliability/ordering, ACK/NAK) and enough of the Bedrock login sequence (resource-pack negotiation, keep-alive pings) to join a local/third-party server running with Xbox Live authentication disabled, presenting a self-signed identity chain. Success = staying connected and idling without being dropped.

Confirmed end-to-end against a real Bedrock Dedicated Server (BDS) test container: `RequestNetworkSettings` → `Login` → encryption handshake → `PlayStatus` → `ResourcePacksInfo` → `ResourcePackStack` → `StartGame` → the real client-side spawn sequence (`ItemRegistry` → `RequestChunkRadius` → `ChunkRadiusUpdated` + `PlayStatus(PlayerSpawn)` → `SetLocalPlayerAsInitialized`) → `Spawned`, then indefinite idle with keep-alive pings and no disconnect. Server log shows a real join and, critically, `Player Spawned: <username>` (not just a session-opened line) - see [`docs/notes/bedrock-login-design.md`](docs/notes/bedrock-login-design.md) for the two bugs found and fixed to get here (`ClientData.ThirdPartyName` needing to equal the username, and the real client-side spawn handshake replacing an earlier premature `SetLocalPlayerAsInitialized`). This was previously believed done twice before - once against an earlier test server that was retired (see [`.skills/bedrock-integration-testing`](.skills/bedrock-integration-testing/SKILL.md)), and once believing `Login` acceptance alone was sufficient - neither carried over; this result is the first verified all the way to a real server-side spawn confirmation.

## Milestone 2 — Xbox Live / Microsoft authentication (done)

OAuth device-code sign-in against the user's Microsoft account, a SISU-based XBL/XSTS exchange, PlayFab login, and a franchise-service session/multiplayer token: the real six-hop flow (not the one-line spec originally sketched here), confirmed from reference source and real server errors. Enables connecting to real (online-mode) servers via `[Auth] Mode=Microsoft` in `BedrockConsoleClient.ini`; `SelfSigned` (Milestone 1's identity) stays the default.

Confirmed end-to-end against real BDS with a cached Microsoft sign-in: reaches `Spawned` and idles indefinitely, same as Milestone 1 - the `ResourcePacksInfo.Decode` bug (out-of-range length read; it was decoding the trailing texture-pack list's `VarUInt32` count as a fixed `UInt16LE`) and the spawn-sequence fix both apply here too. Server log shows the real Xbox Live gamertag and XUID, and `Player Spawned: <gamertag>`. Previously integration tested fully end-to-end against an earlier test server that has since been retired (see [`docs/notes/bedrock-xbox-live-auth-design.md`](docs/notes/bedrock-xbox-live-auth-design.md)); that result was historical only until this verification.

## Milestone 3 — Realms support

Realms-scoped XSTS token, `https://pocket.realms.minecraft.net/worlds` listing, join-endpoint address resolution, then falling into the Milestone 2 connect flow against the resolved address.

## Milestone 4 — reduce build/publish size

Self-contained publish currently ships the whole CoreCLR runtime + BCL per RID (~80MB osx-arm64, ~77MB win-x64, 173 DLLs each) for what's meant to be a lightweight idle-connection client: most of that is `System.Private.CoreLib`/`System.Private.Xml`/etc., not app code. Candidate approach: NativeAOT publish (single native binary, no CoreCLR shipped alongside), the closest .NET equivalent to how a Go binary built on `gophertunnel` would ship, with no separate runtime tree. Needs the codebase checked for AOT-incompatible patterns (reflection-heavy JSON/JWT handling, dynamic loading) before it's a straight swap. Deferred until Milestones 1-3 (protocol/auth correctness) are settled.

See [`docs/notes/bedrock-feasibility.md`](docs/notes/bedrock-feasibility.md) for the technical background behind each milestone.
