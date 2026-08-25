# Bedrock login sequence

Rides inside RakNet's reliable-ordered frames once `RakNetConnection` reaches `Connected` (see [raknet-design.md](raknet-design.md)). `BedrockSession` composes over a `RakNetConnection` and drives its own `BedrockLoginState` state machine — same "explicit enum + guarded transition" pattern as RakNet, for the same reason (a linear sequence, one expected packet per phase). Kept as a separate enum rather than extending RakNet's `ConnectionState`, since that one correctly stops meaning anything once `Connected` — the Bedrock layer's state starts from there.

Reference source: [pmmp/BedrockProtocol](https://github.com/pmmp/BedrockProtocol) and [pmmp/PocketMine-MP](https://github.com/pmmp/PocketMine-MP), the libraries the local test server actually runs, cloned locally at `~/Minecraft/pmmp-reference/` (alongside `RakLib`) for fast lookup instead of one-off fetches. Every wire-format detail below came from reading that source directly, not from memory or the reference clients' (gophertunnel, bedrock-protocol) conventions where those diverged.

## Wire format

- **Packet batch**: `0xFE` + [compression-algorithm byte, once negotiated] + compressed-or-raw payload, where the payload is a flat run of `(VarUInt32 length, bytes)` game packets. Every packet is length-delimited before any packet-specific decode happens, so leaving most of a packet unparsed (see `StartGame` below) can never desync the stream.
- **Compression**: `Zlib` (id 0) is raw deflate, not zlib-wrapped — `DeflateStream`, not `ZLibStream`. Negotiated via `RequestNetworkSettings`/`NetworkSettings`, both sent unencrypted and uncompressed before anything else.
- **VarInt-heavy**, unlike RakNet's fixed-width fields — a separate `BedrockVarIntReader`/`Writer` exists for this reason, distinct from `RakNetSpanReader`/`Writer`.

## Four real bugs, caught by testing against the actual server, not guessed away

1. **Identity JWT was missing `mid`.** First attempt reused `leguuid`/`xname`/`cpk` from reference-client conventions. PMMP's error named the exact missing property (`SelfSignedJwtBody::$mid`) — fixed by reading the real class instead of guessing a second time.
2. **`ClientData` JWT had a wrong field name and was missing ~10 required fields.** `ThirdPartyNameOnly` doesn't exist in the schema at all (the real property is `ThirdPartyName`); `ClientEditorConnectionIntent`, `ClientIsEditorCapable`, `CompatibleWithClientSideChunkGen`, `FilterProfanity`, `GraphicsMode`, `MaxViewDistance`, `MemoryTier`, `PlatformType`, `TrustedSkin` were all missing. Fixed by fetching `ClientData.php` in full and matching every `@required` field exactly, rather than iterating field-by-field on server error messages.
3. **Identity JWT was missing `aud`.** Assumed optional (checked "if present") from an earlier reading; PMMP's `AuthJwtHelper::validateAuthToken` actually requires it unconditionally, must equal `api://auth-minecraft-services/multiplayer`.
4. **The real bug: AES-CTR keystream wasn't a continuous stream.** PMMP's underlying cipher (`Crypto\Cipher::encryptUpdate`/`decryptUpdate`) is a genuine streaming API — a payload that doesn't end on a 16-byte boundary leaves keystream bytes that carry into the *next* encrypt/decrypt call. The first implementation started every `Encrypt()`/`Decrypt()` call at a fresh block boundary, discarding unused keystream bytes from the previous call. This decrypted the first small packet correctly (coincidence — it happened to need exactly one block) and silently corrupted everything after. Caught by comparing which side (client decrypt vs. server decrypt) actually threw the checksum error — the server's own error message named the exact packet index, proving the bug was in our *send* path, not receive. Fixed by making `CtrKeystreamState` persist a running block-counter-plus-intra-block-offset across calls (`Encryption/CtrKeystreamState.cs`, `Encryption/AesCtrKeystream.cs`).

None of these were guessable from the reference client implementations (gophertunnel, bedrock-protocol) alone — they matched the *general* protocol shape but not PMMP's exact validation requirements or its specific streaming-cipher semantics. Reading PMMP's own source was what actually resolved each one.

## Encryption

Fits between `Login` and `PlayStatus`. Client sends `Login` unencrypted → server sends `ServerToClientHandshake` unencrypted (a JWS: header carries the server's P-384 public key as `x5u`, claims carry a 16-byte `salt`, both standard Base64 not Base64Url) → client verifies it, derives the key, enables its own cipher, sends `ClientToServerHandshake` (empty payload, sent *encrypted*) → server enables its cipher → everything from `PlayStatus(LOGIN_SUCCESS)` onward is encrypted both directions.

Key = `SHA-256(salt(16 bytes) ‖ raw ECDH shared secret(48 bytes for P-384))`, one pass, no HKDF. The client's identity keypair doubles as its ECDH keypair — no separate key is generated. Cipher is AES-256 in what PMMP's own source literally calls `fakeGCM`: real AES-CTR keystream, no auth tag; integrity instead comes from an 8-byte `SHA256(counter ‖ plaintext ‖ key)[0:8]` trailer, `counter` a per-direction monotonic count starting at 0. The CTR block counter (separate from that checksum counter) starts at 2 — mimicking "block 1 reserved for the GCM tag" — and, per bug 4 above, is a continuous per-direction stream, not reset per batch.

## Spawn sequencing

`StartGame` does **not** get followed by a second `PlayStatus(PLAYER_SPAWN)` that the client waits for — confirmed from PMMP's `SpawnResponsePacketHandler`, which is the active packet handler immediately after `StartGame` and does nothing but wait for `SetLocalPlayerAsInitialized` from the client. The client should send that immediately after decoding `StartGame`'s `actorRuntimeId`, not wait for anything further. PMMP's own log already shows the player as joined (`"... logged in with entity id N ..."`) before this even happens — the server considers the player spawned server-side well before the client formally acknowledges it. `PlayStatus.PlayerSpawn` is still handled if it ever arrives (tolerated as a no-op), but it isn't the trigger.

Only `StartGame.actorRuntimeId` is decoded (second field: `actorUniqueId` first, zigzag `VarInt64`, discarded; `actorRuntimeId`, plain `VarUInt64`). Everything else in `StartGame` — block palette, game rules, dozens of other fields — is left unparsed, safely, per the batch framing note above.

## Verified working end-to-end

Full login (`RequestNetworkSettings` → `Spawned`) completes in ~300ms against the local PMMP server. Session then idles indefinitely with healthy `ConnectedPing`/`Pong` — confirmed past 36+ seconds continuous, well beyond the ~10s RakNet-only timeout that motivated this slice (see raknet-design.md). This is Milestone 1's actual success bar, met.
