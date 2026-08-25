# Bedrock Console Client

A minimal console client for Minecraft Bedrock Edition. Its only job is to log in and stay connected (AFK); it doesn't do gameplay automation, world/entity/inventory handling, or any of the broader scope that a full client like [Minecraft-Console-Client](https://github.com/lmcalderon/Minecraft-Console-Client) (Java Edition) carries.

## Scope

Planned, in order:

1. **Milestone 0** (done): a runnable executable that does nothing. It proves the project and toolchain scaffolding work before any networking code exists.
2. **Milestone 1** (done): connect to a local/third-party Bedrock server with Xbox Live authentication disabled ("offline mode"): RakNet handshake + login with a self-signed identity chain.
3. **Milestone 2** (done): Microsoft/Xbox Live sign-in — OAuth device-code flow, XBL/XSTS, PlayFab, and a franchise-service session token — enabling connections to real (online-mode) servers.
4. Microsoft Realms support: Realms API lookup/join before falling into the normal connect flow.

Explicitly out of scope: terrain, physics, inventory, entity tracking, chat bots, scripting. This project only needs to log in and stay connected.

See [`docs/notes/bedrock-feasibility.md`](docs/notes/bedrock-feasibility.md) for the protocol/auth research behind this scope, and [`PLAN.md`](PLAN.md) for milestone details.

## Status

Milestones 0 through 2 done: connects over RakNet, logs in with either a self-signed identity (offline-mode servers) or a real Microsoft/Xbox Live account (online-mode servers), and stays connected indefinitely. See [`PLAN.md`](PLAN.md) for details.

## Build & run

```
dotnet build
dotnet run --project BedrockConsoleClient
```

First run generates `BedrockConsoleClient.ini` next to the built executable, with a `ServerAddress` (host:port), `Username`, and `[Auth] Mode` (`SelfSigned` or `Microsoft`) to edit before pointing it at your own server.
