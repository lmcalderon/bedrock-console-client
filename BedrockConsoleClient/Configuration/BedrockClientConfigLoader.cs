namespace BedrockConsoleClient.Configuration;

public static class BedrockClientConfigLoader
{
  public const string FileName = "BedrockConsoleClient.ini";

  private const string DefaultContent = """
      [Main]
      ; Address of the Bedrock server to connect to, as host:port.
      ServerAddress=127.0.0.1:19132

      ; Username to log in with. Bedrock usernames are capped at 16
      ; characters: letters, numbers, underscores, and spaces only.
      Username=BedrockClient

      [Auth]
      ; "SelfSigned" (default) connects to offline-mode servers with a
      ; throwaway identity - no Microsoft account needed. "Microsoft" signs
      ; in with a real account via Xbox Live, needed for online-mode
      ; servers. When set to Microsoft, Username above is ignored - your
      ; Xbox gamertag is used instead.
      Mode=SelfSigned

      """;

  // Looks next to the running executable by default, so it works the same
  // way whether launched via "dotnet run" or the built binary directly.
  // Writes a default file there if none exists yet, matching the
  // generate-on-first-run experience server.properties/MinecraftClient.ini
  // users already expect.
  public static BedrockClientConfig LoadOrCreateDefault(string? directory = null)
  {
    string path = Path.Combine(directory ?? AppContext.BaseDirectory, FileName);
    if (!File.Exists(path))
    {
      File.WriteAllText(path, DefaultContent);
    }

    var sections = IniFile.Parse(path);
    var main = sections.GetValueOrDefault("Main") ?? [];
    var auth = sections.GetValueOrDefault("Auth") ?? [];

    return new BedrockClientConfig
    {
      ServerAddress = main.GetValueOrDefault("ServerAddress", "127.0.0.1:19132"),
      Username = main.GetValueOrDefault("Username", "BedrockClient"),
      AuthMode = ParseAuthMode(auth.GetValueOrDefault("Mode")),
    };
  }

  // Unrecognized or missing values fall back to SelfSigned, so existing
  // Milestone 1 config files with no [Auth] section keep working unchanged.
  private static BedrockAuthMode ParseAuthMode(string? value) =>
      string.Equals(value, "Microsoft", StringComparison.OrdinalIgnoreCase)
          ? BedrockAuthMode.Microsoft
          : BedrockAuthMode.SelfSigned;
}
