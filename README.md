# Bedrock Console Client

A minimal console client for Minecraft **Bedrock Edition** whose only goal is to log into a server and stay connected (AFK) — no gameplay automation, no world/entity/inventory handling, none of the broader scope that a full client like [Minecraft-Console-Client](https://github.com/lmcalderon/Minecraft-Console-Client) (Java Edition) carries.

## Scope

Planned, in order:

1. **Milestone 0** (current): a runnable executable that does nothing — proves the project/toolchain scaffolding works before any networking code exists.
2. Connect to a local/third-party Bedrock server with Xbox Live authentication disabled ("offline mode"): RakNet handshake + login with a self-signed identity chain.
3. Microsoft/Xbox Live authentication: OAuth device-code/browser sign-in, XSTS token, ECDSA-signed JWT identity chain — enabling connections to real (online-mode) servers.
4. Microsoft Realms support: Realms API lookup/join before falling into the normal connect flow.

Explicitly out of scope: terrain, physics, inventory, entity tracking, chat bots, scripting — this project only needs to log in and stay connected.

See [`docs/notes/bedrock-feasibility.md`](docs/notes/bedrock-feasibility.md) for the protocol/auth research behind this scope, and [`PLAN.md`](PLAN.md) for milestone details.

## Status

Milestone 0 only: a skeleton `net10.0` console app with no networking.

## Build & run

```
dotnet build
dotnet run --project BedrockConsoleClient
```
