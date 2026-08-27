namespace BedrockConsoleClient.Networking.Bedrock.Identity;

using System.Security.Cryptography;

// Real BDS rejects a login outright if any required ClientData field is
// missing. This field set was checked directly against packet captures of a
// real 1.26.44/protocol-2168 client's own ClientData JWT, and separately
// against gophertunnel's own real, successful Login packet captured while
// running it against this project's BDS test container - see
// docs/notes/bedrock-login-design.md. IsPartyLeader/PartyId/PlayFabId are
// present because gophertunnel's own default ClientData always sends them
// (the real client capture omitted them and still worked, so they may not be
// strictly required, but matching the one directly-verified-working
// reference exactly removes that as a variable). ProfileHash (a base64
// 32-byte hash) is the one field an earlier attempt at this schema was
// missing; none of these fields' actual values are otherwise meaningful for
// a self-signed/offline login, only their presence and shape are.
internal static class ClientDataJwt
{
  private const int SkinWidth = 64;
  private const int SkinHeight = 64;

  public static string Build(BedrockKeyPair keyPair, string serverAddress, string gameVersion, string username)
  {
    var header = new { alg = "ES384", x5u = keyPair.PublicKeyBase64Der };

    // No "iat" claim: confirmed from bedrock-protocol's source; including
    // one is a real, silent way to make this fail.
    var payload = new Dictionary<string, object>
    {
      ["AnimatedImageData"] = Array.Empty<object>(),
      ["ArmSize"] = "wide",
      ["CapeData"] = string.Empty,
      ["CapeId"] = string.Empty,
      ["CapeImageHeight"] = 0,
      ["CapeImageWidth"] = 0,
      ["CapeOnClassicSkin"] = false,
      ["ClientEditorConnectionIntent"] = 0,
      ["ClientIsEditorCapable"] = false,
      ["ClientRandomId"] = Random.Shared.NextInt64(),
      ["CompatibleWithClientSideChunkGen"] = false,
      ["CurrentInputMode"] = 1,
      ["DefaultInputMode"] = 1,
      ["DeviceId"] = Guid.NewGuid().ToString("N"),
      ["DeviceModel"] = "BedrockConsoleClient",

      // Win32 (8), not Win10 (7): confirmed both from a real client's own
      // capture and by bisecting real BDS's rejection field-by-field - 7 is
      // rejected outright even though it's a valid value in reference-client
      // enums, real BDS just doesn't accept it (see
      // docs/notes/bedrock-login-design.md).
      ["DeviceOS"] = 8,
      ["FilterProfanity"] = true,
      ["GameVersion"] = gameVersion,
      ["GraphicsMode"] = 0,
      ["GuiScale"] = 0,
      ["IsPartyLeader"] = false,
      ["LanguageCode"] = "en_US",
      ["MaxViewDistance"] = 16,
      ["MemoryTier"] = 1,
      ["OverrideSkin"] = false,
      ["PartyId"] = string.Empty,
      ["PersonaPieces"] = Array.Empty<object>(),
      ["PersonaSkin"] = false,
      ["PieceTintColors"] = Array.Empty<object>(),
      ["PlatformOfflineId"] = string.Empty,
      ["PlatformOnlineId"] = string.Empty,
      ["PlatformType"] = 0,
      ["PlayFabId"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(),
      ["PremiumSkin"] = false,
      ["ProfileHash"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
      ["SelfSignedId"] = Guid.NewGuid().ToString(),
      ["ServerAddress"] = serverAddress,
      ["SkinAnimationData"] = string.Empty,
      ["SkinColor"] = "#0",
      ["SkinData"] = Convert.ToBase64String(BuildFlatSkinPixels()),
      ["SkinGeometryData"] = string.Empty,
      ["SkinGeometryDataEngineVersion"] = string.Empty,
      ["SkinId"] = Guid.NewGuid().ToString(),
      ["SkinImageHeight"] = SkinHeight,
      ["SkinImageWidth"] = SkinWidth,
      ["SkinResourcePatch"] = Convert.ToBase64String(MinimalSkinGeometryJson),

      // Real BDS rejects a self-signed login (reason code 143, not in any
      // published disconnect-reason enum) if this is left empty - found by
      // diffing this project's own Login packet against gophertunnel's real,
      // successful one field-by-field: gophertunnel's defaultClientData
      // unconditionally sets this to the connecting username for every
      // login, self-signed or not (see minecraft/dial.go), and real BDS
      // apparently uses it as the actual self-signed display name (the
      // Token JWT's own "xname" claim isn't enough on its own). Harmless for
      // the Xbox Live path too - already confirmed to reach ResourcePacksInfo
      // whether this was empty or not, since a real account's DisplayName
      // from the identity chain takes precedence there regardless.
      ["ThirdPartyName"] = username,
      ["TrustedSkin"] = false,
      ["UIProfile"] = 0,
    };
    return JwtSigner.Sign(header, payload, keyPair.Signing);
  }

  private static ReadOnlySpan<byte> MinimalSkinGeometryJson =>
      "{\"geometry\":{\"default\":\"geometry.humanoid.custom\"}}"u8;

  private static byte[] BuildFlatSkinPixels()
  {
    var pixels = new byte[SkinWidth * SkinHeight * 4];
    for (int i = 0; i < pixels.Length; i += 4)
    {
      pixels[i] = 255;
      pixels[i + 1] = 255;
      pixels[i + 2] = 255;
      pixels[i + 3] = 255;
    }

    return pixels;
  }
}
