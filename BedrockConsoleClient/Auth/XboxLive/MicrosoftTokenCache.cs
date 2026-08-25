namespace BedrockConsoleClient.Auth.XboxLive;

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

// Persists just enough to skip the interactive device-code prompt on
// subsequent runs: the Microsoft refresh token and the Xbox Live proof key
// (the SISU session must keep using the same proof key it authenticated
// with, or every signed request after a restart is rejected). A separate,
// gitignored file next to the executable - not the tracked .ini, since this
// is opaque session state a person never needs to edit, not configuration.
//
// This is a local dev tool's cache, not a hardened credential store: best
// effort is taken to restrict file permissions on platforms that support it,
// but nothing here defends against another local account or process reading
// the file.
internal static class MicrosoftTokenCache
{
  public const string FileName = "BedrockConsoleClient.msa-cache";

  public static MicrosoftTokenCacheEntry? TryLoad()
  {
    string path = Path.Combine(AppContext.BaseDirectory, FileName);
    if (!File.Exists(path))
    {
      return null;
    }

    try
    {
      Stored? stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path));
      if (stored is null || string.IsNullOrEmpty(stored.RefreshToken) || string.IsNullOrEmpty(stored.ProofKeyD))
      {
        return null;
      }

      ECDsa proofKey = XboxLiveProofKey.FromPrivateKeyD(Convert.FromBase64String(stored.ProofKeyD));
      return new MicrosoftTokenCacheEntry(stored.RefreshToken, proofKey);
    }
    catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException)
    {
      // A corrupted or foreign cache file falls back to interactive sign-in
      // rather than crashing the client.
      return null;
    }
  }

  public static void Save(string refreshToken, ECDsa proofKey)
  {
    string path = Path.Combine(AppContext.BaseDirectory, FileName);
    string json = JsonSerializer.Serialize(new Stored(refreshToken, Convert.ToBase64String(XboxLiveProofKey.ExportPrivateKeyD(proofKey))));
    File.WriteAllText(path, json);

    if (!OperatingSystem.IsWindows())
    {
      try
      {
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
      }
      catch (IOException)
      {
        // Best effort only - see the type-level remarks.
      }
    }
  }

  private sealed record Stored(
      [property: JsonPropertyName("refreshToken")] string RefreshToken,
      [property: JsonPropertyName("proofKeyD")] string ProofKeyD);
}
