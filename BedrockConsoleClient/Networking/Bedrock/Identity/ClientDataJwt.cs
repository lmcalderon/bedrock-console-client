namespace BedrockConsoleClient.Networking.Bedrock.Identity;

// PMMP's ClientData mapper rejects a login outright if any required field is
// missing. This field set is copied exactly from the authoritative source
// (BedrockProtocol's ClientData.php, every @required property), not guessed:
// a first attempt based on reference-client conventions was missing several
// fields and had one outright wrong name (ThirdPartyNameOnly; the real
// property is ThirdPartyName), caught by PMMP's own specific error messages.
// See docs/notes/bedrock-login-design.md.
internal static class ClientDataJwt
{
  private const int SkinWidth = 64;
  private const int SkinHeight = 64;

  public static string Build(BedrockKeyPair keyPair, string serverAddress)
  {
    var header = new { alg = "ES384", x5u = keyPair.PublicKeyBase64Der };

    // No "iat" claim: a PMMP-specific gotcha confirmed from bedrock-protocol's
    // source; including one is a real, silent way to make this fail.
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
      ["DeviceId"] = Guid.NewGuid().ToString(),
      ["DeviceModel"] = "BedrockConsoleClient",
      ["DeviceOS"] = 7, // Win10 in Bedrock's DeviceOS enum - a safe common default
      ["FilterProfanity"] = true,
      ["GameVersion"] = "1.26.30",
      ["GraphicsMode"] = 0,
      ["GuiScale"] = 0,
      ["LanguageCode"] = "en_US",
      ["MaxViewDistance"] = 16,
      ["MemoryTier"] = 1,
      ["OverrideSkin"] = false,
      ["PersonaPieces"] = Array.Empty<object>(),
      ["PersonaSkin"] = false,
      ["PieceTintColors"] = Array.Empty<object>(),
      ["PlatformOfflineId"] = string.Empty,
      ["PlatformOnlineId"] = string.Empty,
      ["PlatformType"] = 0,
      ["PremiumSkin"] = false,
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
      ["ThirdPartyName"] = string.Empty,
      ["TrustedSkin"] = true,
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
