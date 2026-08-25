# Configuration: basic tier built, advanced tier still deferred

## What's built

`BedrockConsoleClient.ini` ([Configuration/](../../BedrockConsoleClient/Configuration/)), a `[Main]` section with `ServerAddress` (host:port) and `Username`. Loaded by `BedrockClientConfigLoader.LoadOrCreateDefault()`, which looks next to the running executable (`AppContext.BaseDirectory`, not the current working directory, so it behaves the same whether launched via `dotnet run` or the built binary) and writes a default file there if none exists yet — the same generate-on-first-run experience `server.properties`/`MinecraftClient.ini` users already know.

Hand-rolled INI parser (`IniFile.cs`), no external dependency, no JSON/YAML: this project's target audience already knows the `key=value` format from `server.properties` and MCC's `MinecraftClient.ini`, and a config format that looks unfamiliar is a real adoption barrier for that audience in a way it wouldn't be for a typical dev tool.

## The split

Two tiers, not one flat settings file:

- **Basic** (built): what every user sets — server address, username. Later, a Microsoft account for Milestone 2.
- **Advanced** (still deferred): protocol-tuning knobs like resend backoff, MTU candidates, handshake timeouts — genuinely useful for someone on a laggy connection who wants more time to connect, but not something a typical user should need to touch. `RakNetConnectionOptions` ([Networking/RakNet/RakNetConnectionOptions.cs](../../BedrockConsoleClient/Networking/RakNet/RakNetConnectionOptions.cs)) is already shaped as this eventual object; nothing populates it from a file yet.

Model: Minecraft-Console-Client's `MinecraftClient.ini`, which keeps a `[Main]` section for server/account separate from per-feature sections. Same idea here — `[Main]` now, an `[Advanced]` section (or separate file) later, so the common case stays simple.

## Why the advanced tier is still deferred

Building a config surface for tuning knobs nobody has asked to change yet is speculative. Revisit when a real laggy-connection scenario (or Milestone 2/3 needing its own knobs) actually asks for it.
