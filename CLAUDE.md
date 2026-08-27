# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A from-scratch .NET console client for Minecraft Bedrock Edition that logs in and stays connected (AFK) - no gameplay automation, world/entity/inventory handling, or the broader scope of the sibling project [Minecraft-Console-Client](https://github.com/lmcalderon/Minecraft-Console-Client) (Java Edition). Every layer is hand-rolled: RakNet transport, JWT signing, AES-CTR encryption, and the Xbox Live/XSTS/PlayFab auth chain are all implemented directly against the BCL with zero NuGet dependencies (see `BedrockConsoleClient.csproj`). See `PLAN.md` for the milestone roadmap and `docs/notes/bedrock-feasibility.md` for the protocol/auth research behind it.

## Commands

```
dotnet build
dotnet run --project BedrockConsoleClient
```

There is no test project - correctness is established by integration testing against a real Bedrock Dedicated Server, not unit tests. See "Integration testing" below before claiming any networking/protocol change works.

First run generates `BedrockConsoleClient.ini` next to the built executable (`ServerAddress`, `Username`, `[Auth] Mode` = `SelfSigned` or `Microsoft`). Point it at a real server before running.

Code style is enforced at build time via `Directory.Build.props` (StyleCop.Analyzers + Microsoft.VisualStudio.Threading.Analyzers, `EnforceCodeStyleInBuild=true`) - a normal `dotnet build` will fail on style/threading violations, not just report them. Naming conventions and StyleCop rule overrides are in `.editorconfig`; the full rationale for each override is documented there. `.skills/csharp-best-practices/SKILL.md` and `.skills/dotnet-security-review/SKILL.md` hold the broader (org-wide, not project-specific) C# conventions this project follows.

## Integration testing

This is the load-bearing discipline for this repo: **reference-client source (gophertunnel, bedrock-protocol) is a source of hypotheses, never proof.** Only a real Bedrock Dedicated Server's actual on-the-wire behavior counts. Every protocol bug documented in `docs/notes/` was found by running the real reference client (`gophertunnel`, cloned at `~/Minecraft/bedrock-reference/`) against this project's own BDS test container and diffing real bytes, not by reasoning about the spec harder.

Read `.skills/bedrock-integration-testing/SKILL.md` before touching anything under `Networking/` - it documents the local BDS test setup (Docker/Colima, the UDP port-forwarder gotcha) and the four test modes (RakNet handshake, server-initiated disconnect, full login + idle, Xbox Live sign-in) with their pass criteria. Never claim a networking change works from build success or static reasoning alone.

## Architecture

### Composition root

`Program.cs` is a top-level-statements script, not a class. Order matters and is deliberate: config load -> Xbox Live sign-in (if `Mode=Microsoft`, before opening any connection, since interactive device-code sign-in can take far longer than the login handshake's timeout allows mid-handshake) -> RakNet connect -> Bedrock login -> idle until Ctrl+C or server-initiated disconnect (`cts.Cancel()` is wired to both).

### Two independent state machines, same pattern

`RakNetConnection` (transport: `Unconnected -> OfflineHandshake1 -> OfflineHandshake2 -> ConnectedHandshake -> Connected -> Disconnected`) and `BedrockSession` (application-layer login sequence) each use the same "explicit enum + `Dictionary<TState, TState[]>` legal-transitions table + guarded `TransitionTo`" pattern rather than a class-per-state. Both are linear sequences where every state waits on exactly one expected packet type, so the enum-driven variant is the deliberate, documented choice over GoF-style per-state polymorphism - see `docs/notes/raknet-design.md` for why, and revisit only if a state gains real independent behavior of its own. They are kept as two separate enums (not one shared `ConnectionState`) because RakNet's state correctly stops meaning anything once `Connected`; the Bedrock layer's state starts from there. `BedrockSession` composes over an already-`Connected` `RakNetConnection`, subscribing to its `GamePacketReceived`/`StateChanged` events rather than the two being merged.

### RakNet layer (`Networking/RakNet/`)

Implements enough of RakNet to be a real client, not just a UDP socket: the offline handshake (MTU probing, the anti-amplification cookie), the connected handshake, and the reliability layer (`Reliability/`: datagrams, frames, ACK/NAK, resend with exponential backoff, split-packet reassembly). Only order channel 0 is used, so ordering is a single expected-index counter rather than per-channel tracking. `RakNetClient` is the static entry point (`ConnectAsync`, `QueryServerAsync` for an unconnected MOTD ping). Wire-format details (frame length in bits, 24-bit fields little-endian vs. everything else big-endian, address octet bit-flipping) were all confirmed against `sandertv/go-raknet` and empirically against real BDS - see `docs/notes/raknet-design.md`.

### Bedrock login layer (`Networking/Bedrock/`)

`BedrockSession` drives the full login sequence: `NetworkSettings` negotiation (compression) -> `Login` -> encryption handshake -> `PlayStatus`/`ResourcePacksInfo`/`ResourcePackStack` -> `StartGame` -> the real spawn handshake (`ItemRegistry` -> client sends `RequestChunkRadius` -> both `ChunkRadiusUpdated` and `PlayStatus(PlayerSpawn)` must arrive, order unspecified, tracked as two independent booleans rather than further states -> client sends `SetLocalPlayerAsInitialized`, which is what actually completes the spawn server-side). See `docs/notes/bedrock-login-design.md` for the full packet sequence and every real bug found getting it working (JSON key ordering, `ClientData.DeviceOS`/`ThirdPartyName` values BDS silently requires, `ResourcePacksInfo`'s VarUInt32-prefixed list vs. a wrongly-assumed fixed `UInt16LE`).

- `Encryption/`: AES-256 in what the protocol calls `fakeGCM` - real AES-CTR keystream (`.NET`'s `Aes` has no native CTR mode, so this XORs a manually generated keystream), no auth tag; integrity comes from an 8-byte SHA-256 trailer instead. Key derivation is one-pass `SHA-256(salt || raw ECDH shared secret)`, no HKDF.
- `Identity/`: `IIdentityChainProvider` is the Strategy seam between `BedrockSession` and how the Login packet's identity chain gets produced - `SelfSignedIdentityChainProvider` (offline-mode) and `XboxLiveIdentityChainProvider` (real Microsoft account) are the two implementations, selected by `[Auth] Mode` in the ini.
- `Packets/`: one file per packet, each documenting its direction (`Client -> server only` / `Server -> client only`) and any wire-format gotcha found against real BDS.

### Xbox Live / Microsoft auth (`Auth/XboxLive/`)

The real login flow is six HTTP hops, not the three a first pass at the reference source suggested: OAuth device-code sign-in -> SISU-based XBL/XSTS token -> PlayFab login -> Minecraft service discovery -> a franchise-service session token -> a multiplayer token (this last one is what actually goes in the Login packet's `Token` field). Steps 1-5 don't depend on any specific connection and run once, up front, in `Program.cs`; only the multiplayer token (and a separate identity-chain request) happen per-connection inside `XboxLiveIdentityChainProvider.ResolveAsync`. `MicrosoftTokenCache` persists the OAuth refresh token and proof key (not the short-lived derived tokens) to a gitignored `BedrockConsoleClient.msa-cache` file. See `docs/notes/bedrock-xbox-live-auth-design.md` for the full hop-by-hop breakdown, including two real bugs (PlayFab needing the title-specific NSAL endpoint table, not the generic one; PlayFab's response envelope key being `data` vs. the franchise services' `result`) found by reading actual server error responses.

### Configuration (`Configuration/`)

`BedrockClientConfigLoader.LoadOrCreateDefault` reads `BedrockConsoleClient.ini` next to the running executable, writing a default file on first run. Unrecognized/missing `[Auth] Mode` values fall back to `SelfSigned` so older config files keep working unchanged.
