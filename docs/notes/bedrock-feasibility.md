# Bedrock Edition login: protocol and auth notes

Notes on what a login-and-idle Bedrock Edition client actually requires, gathered before starting implementation.

## Transport and packet protocol

Bedrock Edition runs over **RakNet** (UDP), not Java Edition's raw TCP stream. A client needs its own RakNet implementation: connection handshake, reliability/ordering channels, packet splitting/reassembly, and ACK/NAK handling. Bedrock's packet IDs, structure, and per-batch compression (zlib/snappy) are entirely separate from Java Edition's protocol — there is nothing to port from a Java-focused client, this is a ground-up stack.

Reference implementations worth studying: `gophertunnel` (Go), `bedrock-protocol` (Node.js).

## Authentication

Login uses Xbox Live / Microsoft auth, sharing the same first step as Java Edition auth:

1. OAuth device-code or browser sign-in against the user's Microsoft account, producing an Xbox Live (XBL) user token. This part is identical to Java Edition's Microsoft auth flow.
2. From there it diverges:
   - **Java Edition** requests an XSTS token with `RelyingParty: "rp://api.minecraftservices.com/"`, then exchanges that token via a REST call to `api.minecraftservices.com` for a Java-specific bearer token used for profile/session calls.
   - **Bedrock Edition** requests an XSTS token with a different relying party (`rp://multiplayer.minecraft.net/`). There is no bearer-token exchange. Instead, the client generates its own ECDSA P-384 keypair and uses the XSTS token to obtain a signed JWT "identity chain" proving the Xbox identity is tied to that public key. That chain is sent in-band inside the RakNet login packet when connecting to a server, and the client signs further JWTs with its private key during the handshake.

Same Microsoft account and same first OAuth hop, but the resulting token and the client-side crypto needed to use it are not interchangeable between Java and Bedrock.

## Encryption

Bedrock negotiates AES-GCM/CFB via ECDH key exchange derived from the login JWT chain, distinct from Java Edition's RSA-based handshake.

## Login isn't fully passive

Even a client that only wants to idle still has to participate in the handshake: complete resource-pack negotiation packets and respond to periodic pings within the RakNet session, or the server will drop the connection. A minimal AFK client needs a real (if partial) RakNet + login state machine, not just an open socket.

## Server types

- **Local / third-party servers** (self-hosted Bedrock Dedicated Server, PocketMine-MP, Nukkit, community hosts): direct IP:port connection, no additional API layer. Many such servers support disabling Xbox Live authentication ("offline mode"), in which case the client can present a self-signed identity chain instead of a real Microsoft/XBL-signed one — a good way to get RakNet + login working before any real auth code exists. See [`.skills/bedrock-integration-testing`](../../.skills/bedrock-integration-testing/SKILL.md) for concrete macOS setup steps (PocketMine-MP, since official BDS is Windows/Linux only).
- **Microsoft Realms**: not a plain IP:port. Requires an XSTS token scoped to the Realms relying party, a call to the Realms API (`https://pocket.realms.minecraft.net/worlds`) to list realms the account owns or was invited to, then a join call to resolve a live connection address/session — only after that does the normal RakNet handshake + login proceed. Treat as its own milestone, later than direct-connect support.

## Mobile client (aside)

The core protocol logic (RakNet, crypto, JWT handling) is portable .NET and could target Android/iOS via MAUI. The practical blocker is sustained background execution, especially on iOS, which aggressively kills background sockets outside a few unrelated background modes. Android is more permissive via a foreground service, but still fights battery optimization. If "AFK from my phone" is the actual goal, the simpler pattern is running the always-on client on a server/VPS and using a phone only as a remote control/dashboard, not as the machine holding the connection.
