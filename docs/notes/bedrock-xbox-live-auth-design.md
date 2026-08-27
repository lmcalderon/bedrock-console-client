# Xbox Live authentication

Adds a second `IIdentityChainProvider` (see [bedrock-login-design.md](bedrock-login-design.md) for the seam itself) that signs in with a real Microsoft account instead of presenting a throwaway self-signed identity. Selected via `[Auth] Mode=Microsoft` in `BedrockConsoleClient.ini`; `SelfSigned` stays the default and is unchanged.

Reference source: [Sandertv/gophertunnel](https://github.com/Sandertv/gophertunnel), [df-mc/go-xsapi](https://github.com/df-mc/go-xsapi) (the SISU/XAL/XSTS protocol layer gophertunnel builds on), and [df-mc/go-playfab](https://github.com/df-mc/go-playfab), all cloned locally at `~/Minecraft/bedrock-reference/` for direct reading rather than one-off fetches. Every wire-format detail below came from reading that source, not from the milestone's original one-line spec or an earlier draft of this design that had to be corrected against real server errors (see below).

## The real flow is six hops, not three

An early pass at this design (based on `gophertunnel/minecraft/auth`'s `RequestMinecraftChain` in isolation) concluded the flow was just OAuth → XBL/XSTS → one POST to `multiplayer.minecraft.net/authentication`, and that PlayFab/service-discovery/session-token exchange was NetherNet-only plumbing, irrelevant to a plain RakNet connection. That was wrong, discovered directly from a real server error (below) rather than from source alone. The two things it conflated were "used to dial the network transport" (`DialContextIdentity`, genuinely NetherNet-only) and "embedded in the Login packet" (used regardless of transport). The full chain:

1. **OAuth sign-in**, a standard RFC 8628 device-code flow (`login.live.com/oauth20_connect.srf` + `oauth20_token.srf`, not `login.microsoftonline.com` - Bedrock uses the older Xbox/Live Connect endpoints) against Bedrock's own client ID (`0000000048183522`, the Android config, see below), scope `service::user.auth.xboxlive.com::MBI_SSL`.
2. **XBL/XSTS token** via a SISU session: a device token (XASD), then one `sisu.xboxlive.com/authorize` call that returns a title token (XAST), a user token (XASU), and an XSTS token for the default relying party (`http://xboxlive.com`) together.
3. **PlayFab login** (`Client/LoginWithXbox`) using a second XSTS token scoped to PlayFab's relying party, producing a PlayFab session ticket.
4. **Service discovery** (`client.discovery.minecraft-services.net`) resolving the "franchise" authorization service's base URI for the current client version.
5. **Session token**: the PlayFab session ticket exchanged at `<serviceUri>/api/v1.0/session/start` for an `authorizationHeader`.
6. **Multiplayer token**: that session token plus the connection's own P-384 public key, exchanged at `<serviceUri>/api/v1.0/multiplayer/session/start`, returns a `signedToken` - this is what the Login packet's `Token` field actually is.

Steps 1-5 don't depend on any particular connection and run once, up front, in `Program.cs` before `RakNetClient.ConnectAsync`, since interactive sign-in can take minutes, far longer than `BedrockLoginOptions.LoginTimeout` (15s) allows mid-handshake. Step 6, plus the separate identity-chain request below, are the only things `ResolveAsync` does per connection, both fast enough to fit comfortably inside that timeout.

## The Login packet's two identity fields

`AuthInfoJson` can carry two *separate* pieces of identity, not one:

- **`Certificate`**: when present, a JSON-encoded **string** (not a nested object - real Bedrock double-encodes this) of `{"chain":["jwt0","jwt1"]}`, obtainable from a single POST to `multiplayer.minecraft.net/authentication` with just `{"identityPublicKey": "<connection's P-384 public key>"}`.
- **`Token`**: the multiplayer/session token from step 6 above, unrelated to the chain.

A packet capture of a real signed-in client's own `Login` packet (a real client added as an external server against this project's local BDS test container) settled which of these real BDS actually expects for `AuthenticationType.FULL`: `{"AuthenticationType":0,"Token":"<jwt>"}` only - no `Certificate` field at all. `XboxLiveIdentityChainProvider.ResolveAsync` matches this exactly.

`AuthenticationType` is `0` (`FULL`) - distinct from `SelfSignedIdentityChainProvider`'s `2` (`SELF_SIGNED`).

## Two real bugs, caught by testing against the actual server, not guessed away

1. **PlayFab's relying party isn't in the generic NSAL table.** The SISU authorize call's signature policy resolves from `title.mgt.xboxlive.com/titles/default/endpoints`, a public, unauthenticated table covering generic `*.xboxlive.com` services. Reusing that same table for PlayFab's `Client/LoginWithXbox` call failed with "No NSAL title endpoint matched" against the real endpoint. `go-xsapi`'s own doc comments say as much ("for title-specific services such as PlayFab or Minecraft Realms, `Current` or `Title` should be used") but this was missed on the first pass. Fixed by adding `XboxLiveTitleEndpoints.ResolveForCurrentTitleAsync`, which fetches `title.mgt.xboxlive.com/titles/current/endpoints` instead, authenticated with the default-relying-party XSTS token and signed with the proof key, unlike the public default table.
2. **PlayFab's response envelope key is `data`, not `result`.** The franchise/discovery services (session/start, multiplayer/session/start, service discovery) all wrap their payload as `{"result": {...}}` (confirmed from `gophertunnel/minecraft/service/internal/result.go`). PlayFab's own native API wraps it as `{"data": {...}}` instead (confirmed from `go-playfab/internal/result.go`) - a different service family with its own convention. Reusing the franchise envelope shape for the PlayFab response deserialized `Result` as null and threw a `NullReferenceException` on the first field access. Fixed by using the correct envelope per service, and by making every envelope's nested payload nullable so a shape mismatch throws a clear `XboxLiveAuthException` instead of an NRE.

Both were resolved by reading the actual response and the actual failure produced (an exact "no match" exception, a stack trace pointing at a specific null-dereference line) against real Microsoft/Xbox Live/PlayFab endpoints, not by re-reading source a second time and guessing better.

## Request signing

Xbox Live signs most authenticated requests: a `Signature` header derived from a P-256 "proof key" (`XboxLiveProofKey`, deliberately separate from `BedrockKeyPair`'s P-384 connection key, since Xbox Live's signing scheme requires P-256 specifically) over a SHA-256 hash of policy version, timestamp, method, path, the `Authorization` header value, and the body, each field null-byte-separated (`XboxLiveRequestSigner`, ported from `go-xsapi/xal/internal/signature.go`). The signature itself is raw `r‖s` (64 bytes, zero-padded), not the DER encoding `ECDsa.SignHash` produces by default; `DSASignatureFormat.IeeeP1363FixedFieldConcatenation` is required. Two exceptions, both confirmed from source rather than assumed: the identity-chain request (`multiplayer.minecraft.net/authentication`) explicitly omits `Signature`, and PlayFab login isn't signed at all - the XSTS token is simply carried as a string in the request body.

## Device identity: Android, not Windows

`BedrockXboxLiveConfig` uses gophertunnel's `AndroidConfig` (client ID `0000000048183522`, title ID `1739947436`) rather than a desktop identity. gophertunnel's own `Win32Config` is documented as non-functional ("retrieving the RPS ticket required for device token requests is not yet known"), so impersonating a mobile device is the only proven-working path available in the reference implementation, not a shortcut unique to this project.

## Caching

`MicrosoftTokenCache` persists the OAuth refresh token and the P-256 proof key (not the derived Xbox Live/PlayFab/session tokens, which are cheap to re-derive and short-lived) to a gitignored file next to the executable, `BedrockConsoleClient.msa-cache`, with best-effort owner-only permissions on platforms that support it. The proof key must be reused across a refresh, not regenerated - Xbox Live's tokens are bound to the key that originally requested them.

## Status

Interactive device-code sign-in completes, and `Signed in to Xbox Live as <real gamertag>` logs. A second run using the cached refresh token (no prompt) reproduces the same result non-interactively.

Re-verified end-to-end against real BDS with a cached refresh token: reaches `Spawned` and idles indefinitely with no disconnect, same as the self-signed path - see [`bedrock-login-design.md`](bedrock-login-design.md) for the (unrelated-to-auth) bugs that blocked getting all the way there. Server log shows the real gamertag and XUID, and `Player Spawned: <gamertag>`. This supersedes the earlier end-to-end verification against a now-retired test server - see [`.skills/bedrock-integration-testing`](../../.skills/bedrock-integration-testing/SKILL.md).
