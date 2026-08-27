# Bedrock login sequence

Rides inside RakNet's reliable-ordered frames once `RakNetConnection` reaches `Connected` (see [raknet-design.md](raknet-design.md)). `BedrockSession` composes over a `RakNetConnection` and drives its own `BedrockLoginState` state machine, the same "explicit enum + guarded transition" pattern as RakNet, for the same reason (a linear sequence, one expected packet per phase). Kept as a separate enum rather than extending RakNet's `ConnectionState`, since that one correctly stops meaning anything once `Connected` - the Bedrock layer's state starts from there.

The only reference that counts for this project is the real Bedrock Dedicated Server (BDS) itself: its actual on-the-wire behavior, and packet captures of real clients talking to it. Reference-client source (gophertunnel, bedrock-protocol) is useful for hypotheses to test, never treated as proof by itself.

## Wire format

- **Packet batch**: `0xFE` + [compression-algorithm byte, once negotiated] + compressed-or-raw payload, where the payload is a flat run of `(VarUInt32 length, bytes)` game packets. Every packet is length-delimited before any packet-specific decode happens, so leaving most of a packet unparsed (see `StartGame` below) can never desync the stream.
- **Compression**: `Zlib` (id 0) is raw deflate, not zlib-wrapped - `DeflateStream`, not `ZLibStream`. Negotiated via `RequestNetworkSettings`/`NetworkSettings`, both sent unencrypted and uncompressed before anything else. Confirmed against real BDS: the negotiated threshold round-trips correctly, and both compressed and forced-uncompressed Login packets reach the server intact.
- **VarInt-heavy**, unlike RakNet's fixed-width fields. That's why a separate `BedrockVarIntReader`/`Writer` exists, distinct from `RakNetSpanReader`/`Writer`.

## ClientData JWT schema - the actual `Login` rejection root cause

Real BDS was rejecting *every* `Login` attempt outright with `PacketViolationWarning` (`PacketId: 1`, message `Connection Request invalid. readNoHeader failed! packetId: 1`) - regardless of identity chain shape (self-signed or real Xbox Live), `Certificate` presence, or compression. This was eventually root-caused to a single field: **`ClientData.DeviceOS` was `7` (`Win10` in reference-client enums), and real BDS rejects `7` outright.** `8` (`Win32`) is what a real client actually sends and what real BDS accepts - confirmed both from a real client's own packet capture and by field-by-field bisection against real BDS (see method below). Every other field name, order, and type was already correct; this one value was the entire blocker.

**How this was found**, since the method matters more than the specific field (the next wrong value won't be this one): reference-client source (gophertunnel, bedrock-protocol) can tell you a plausible schema, but only real BDS's response is proof. The decisive steps, in order:

1. A real 1.26.44/protocol-2168 client's own `Login` packet was captured (Wireshark, client added as an external server against this project's local BDS test container) and decoded field-by-field. This fixed the `ClientData` field *set* (added a missing `ProfileHash`) but not the actual bug.
2. Running `gophertunnel` itself (the real, actively-maintained reference client) against this same BDS container succeeded outright for a self-signed login - proving the container/environment/protocol-version pairing was never the issue, and giving a second, independently-obtainable known-good `Login` packet to diff against (captured via its `Dialer.PacketFunc` hook).
3. Sending gophertunnel's exact captured bytes through this project's own RakNet/transport layer also succeeded (reached `ServerToClientHandshake`) - proving this project's transport was never the bug either, only content generation.
4. With transport and schema both cleared, a bisection harness (re-signing gophertunnel's known-good `ClientData` payload with this project's own key, then swapping in this project's own generated value for one field/group of fields at a time) narrowed the *specific* rejected value down field-by-field until `DeviceOS` alone reproduced the rejection.

Also confirmed in the same pass: real BDS's JSON parsing for the outer `AuthInfoJson` is **order-sensitive** - `Certificate` (when present) must be serialized before `AuthenticationType`/`Token`, and every JWT's claims must be serialized in the same order gophertunnel's own `go-jose`-based signing produces (alphabetical, since it round-trips claims through a merged map) - see `BedrockSession.SendLoginAsync` and `Identity/SelfSignedIdentityChain.cs`.

## Self-signed (offline-mode) identity chain - the `Disconnect(143)` root cause

With the `DeviceOS` fix, `Login` itself was accepted for the self-signed path (no more `PacketViolationWarning`), but the server still sent a `Disconnect` with reason code `143` before resource-pack negotiation - `143` was (and still is) out of range of every disconnect-reason enum available in gophertunnel source, including a fresh `v1.59.0` checkout (max defined value `138`), so it was never going to be identified by looking it up.

**How this was actually found**, since guessing at the meaning of an unlisted reason code doesn't work: gophertunnel's own `minecraft.Dialer` was pointed at this project's BDS container with a self-signed (no `TokenSource`) dial, and it **spawned successfully** (`Player Spawned: Steve` server-side) - proving the server does accept self-signed logins in general, so the bug was this project's own request content, not policy. From there:

1. Its `PacketFunc` hook captured the real `Login` packet's exact bytes.
2. Replaying those exact captured bytes through this project's own RakNet/transport layer also succeeded (reached `ServerToClientHandshake` and beyond) - ruling out this project's transport/framing as the bug, same conclusion as the earlier `DeviceOS` investigation.
3. Field-by-field decoding of the captured `AuthInfoJson`/`Token`/`ClientData` against this project's own generated equivalents (same key set, same order, structurally identical) found no difference - so the bug had to be a *value*, not the schema.
4. Mixing this project's own `AuthInfoJson`/Token with gophertunnel's `ClientData` (and vice versa) both failed with a *different*, in-range reason (`46`, `NotAuthenticated`) - confirming real BDS cross-validates that `ClientData`'s signing key matches the identity key referenced in the `Token`, and that this project's own key-matching was already correct (since the pure self-generated pair failed with `143`, not `46`).
5. That narrowed it to a genuine content difference between this project's `ClientData` and gophertunnel's: gophertunnel's `defaultClientData` (`minecraft/dial.go`) unconditionally sets `ClientData.ThirdPartyName` to the connecting username for *every* login, self-signed or not - this project's `ClientDataJwt.Build` always sent it empty. For a self-signed identity, the `Token` JWT's own `xname` claim apparently isn't treated as authoritative by real BDS on its own; `ThirdPartyName` has to carry it too.

Fixed by threading the username into `ClientDataJwt.Build` and always setting `ThirdPartyName` to it (matching gophertunnel unconditionally, not just for self-signed) - harmless for the Xbox Live path, which already worked with either an empty or populated value there since a real account's chain-verified `DisplayName` takes precedence.

## Encryption

Fits between `Login` and `PlayStatus`. Client sends `Login` unencrypted → server sends `ServerToClientHandshake` unencrypted (a JWS: header carries the server's P-384 public key as `x5u`, claims carry a 16-byte `salt`, both standard Base64 not Base64Url) → client verifies it, derives the key, enables its own cipher, sends `ClientToServerHandshake` (empty payload, sent *encrypted*) → server enables its cipher → everything from `PlayStatus(LOGIN_SUCCESS)` onward is encrypted both directions.

Key = `SHA-256(salt(16 bytes) ‖ raw ECDH shared secret(48 bytes for P-384))`, one pass, no HKDF. The client's identity keypair doubles as its ECDH keypair; no separate key is generated. Cipher is AES-256 in what the protocol itself calls `fakeGCM`: real AES-CTR keystream, no auth tag; integrity instead comes from an 8-byte `SHA256(counter ‖ plaintext ‖ key)[0:8]` trailer, `counter` a per-direction monotonic count starting at 0. The CTR block counter (separate from that checksum counter) starts at 2, mimicking "block 1 reserved for the GCM tag," and is a continuous per-direction stream, not reset per batch (an early implementation reset it per batch instead, which decrypted the first small packet correctly by coincidence and silently corrupted everything after - caught by comparing which side's decrypt actually threw the checksum error).

**Confirmed against real BDS** for the Xbox Live/Microsoft-authenticated path: `ServerToClientHandshake` → `ClientToServerHandshake` → encrypted `PlayStatus`/`ResourcePacksInfo` all arrive correctly. Not yet confirmed for the self-signed path, which currently disconnects before this point - see above.

## Spawn sequencing

An earlier attempt at this (based on an earlier/retired test server, and re-confirmed wrong against real BDS) assumed `StartGame` is followed directly by the client sending `SetLocalPlayerAsInitialized`, with no further round trip. That gets the client into an idle ping loop without being disconnected, which looks like success - but real BDS's own log never records `Player Spawned: <username>` for that connection, only `Player connected`. The player never actually finishes spawning server-side; it just isn't kicked for failing to.

The real sequence, confirmed by diffing against gophertunnel's client-side state machine (`minecraft/conn.go`: `handleStartGame` → `handleItemRegistry` → `handleRequestChunkRadius`/`handleChunkRadiusUpdated` → `handlePlayStatus(PlayerSpawn)` → `tryFinaliseClientConn`) and matched against what actually produces a `Player Spawned` log line against this project's BDS container:

1. `StartGame` arrives - decode `actorRuntimeId`, wait for `ItemRegistry` (not parsed, only its arrival matters here).
2. `ItemRegistry` arrives - send `RequestChunkRadius` (signed `VarInt32` radius + a plain-byte max radius; `16`/`16` mirrors a real client's default). This is also what makes the server start streaming `LevelChunk`/`AddActor`/etc. - expected, not a bug.
3. Wait for **both** `ChunkRadiusUpdated` and `PlayStatus(PlayerSpawn)` to arrive - order between the two isn't guaranteed by the protocol, so this project tracks them as two independent booleans (`BedrockSession._chunkRadiusUpdated`/`_playStatusPlayerSpawnReceived`) rather than encoding it as a strict linear state, mirroring gophertunnel's `tryFinaliseClientConn` pattern.
4. Only once both have arrived does the client send `SetLocalPlayerAsInitialized` - *this* is what completes the spawn and produces the server's `Player Spawned` log line.

Only `StartGame.actorRuntimeId` is decoded (second field: `actorUniqueId` first, zigzag `VarInt64`, discarded; `actorRuntimeId`, plain `VarUInt64`). Everything else in `StartGame` (block palette, game rules, dozens of other fields) is left unparsed, safely, per the batch framing note above.

## `ResourcePacksInfo` decode bug (affected both paths)

`ResourcePacksInfo`'s trailing texture-pack list is `VarUInt32`-count-prefixed (confirmed from gophertunnel's `protocol.Slice`, used for that field), not a fixed `UInt16LE` as an earlier attempt at this schema assumed. Reading it as a fixed 2-byte value desyncs the reader partway through a packet that otherwise decoded fine, throwing an out-of-range error on whatever field is read next - not a `PacketViolationWarning`, since the corruption is purely on this project's own receive side. Both the self-signed and Xbox Live paths hit this once they got far enough to receive the packet, since they share the same decoder.

## `ResourcePackClientResponse` wire format was a full schema mismatch

An earlier attempt at this packet modeled `Status` as a single raw byte, no string, no pack-ID count. Real BDS's decoder is schema-bound and names the field type `ResourcePackResponse` in its own `PacketViolationWarning` text; the actual wire format (confirmed from gophertunnel's `ResourcePackClientResponse.Marshal`) is a `VarUInt32` response code **followed by that same value's string name** (`"cancel"`/`"downloading"`/`"downloadingfinished"`/`"resourcepackstackfinished"`, 0-indexed) - the value is redundantly encoded twice. `PacksToDownload` (a `VarUInt32`-prefixed string list) is only present when the response is `downloading` (`SendPacks`), which this client never sends.

## Status

Confirmed end-to-end against real BDS for both the self-signed and Xbox Live/Microsoft-authenticated paths: `Login` → encryption → `PlayStatus` → `ResourcePacksInfo` → `ResourcePackStack` → `StartGame` → the real spawn handshake above → `Spawned`, with the server logging a real join and `Player Spawned: <username>` in both cases, then indefinite idle with no disconnect. Full login was previously verified end-to-end against an earlier test target that has since been retired in favor of real BDS - see [`.skills/bedrock-integration-testing`](../../.skills/bedrock-integration-testing/SKILL.md) for why; this result supersedes that historical one with a real, current verification.
