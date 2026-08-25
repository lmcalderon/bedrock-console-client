# Milestones

## Milestone 0 — skeleton (current)

A `net10.0` console app (`BedrockConsoleClient/`) that builds, runs, prints a startup message, and exits cleanly on Ctrl+C. No networking, no dependencies beyond the .NET SDK. Purpose: prove the repo/toolchain scaffolding works before any protocol code is written.

## Milestone 1 — offline-mode local connect

Implement a minimal RakNet client (connection handshake, reliability/ordering, ACK/NAK) and enough of the Bedrock login sequence (resource-pack negotiation, keep-alive pings) to join a local/third-party server running with Xbox Live authentication disabled, presenting a self-signed identity chain. Success = staying connected and idling without being dropped.

## Milestone 2 — Xbox Live / Microsoft authentication

OAuth device-code or browser sign-in against the user's Microsoft account, XBL user token, XSTS token scoped to `rp://multiplayer.minecraft.net/`, client-generated ECDSA P-384 keypair, signed JWT identity chain. Enables connecting to real (online-mode) servers.

## Milestone 3 — Realms support

Realms-scoped XSTS token, `https://pocket.realms.minecraft.net/worlds` listing, join-endpoint address resolution, then falling into the Milestone 2 connect flow against the resolved address.

See [`docs/notes/bedrock-feasibility.md`](docs/notes/bedrock-feasibility.md) for the technical background behind each milestone.
