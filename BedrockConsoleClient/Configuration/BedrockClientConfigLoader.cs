namespace BedrockConsoleClient.Configuration;

public static class BedrockClientConfigLoader
{
  public const string FileName = "BedrockConsoleClient.ini";

  private const string DefaultContent = """
      [Main]
      ; Bedrock server to connect to, as host:port.
      ServerAddress=127.0.0.1:19132

      ; Login username (max 16 characters). Ignored if Mode=Microsoft below.
      Username=BedrockClient

      [Auth]
      ; SelfSigned (default): offline-mode servers, no Microsoft account.
      ; Microsoft: signs in via Xbox Live for online-mode servers, using
      ; your gamertag instead of Username above.
      Mode=SelfSigned

      [Diagnostics]
      ; Logs each packet's ID and size as it's sent/received - handy when
      ; reporting a connection problem.
      Verbose=false

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
    var diagnostics = sections.GetValueOrDefault("Diagnostics") ?? [];

    return new BedrockClientConfig
    {
      ServerAddress = main.GetValueOrDefault("ServerAddress", "127.0.0.1:19132"),
      Username = main.GetValueOrDefault("Username", "BedrockClient"),
      AuthMode = ParseAuthMode(auth.GetValueOrDefault("Mode")),
      Verbose = string.Equals(diagnostics.GetValueOrDefault("Verbose"), "true", StringComparison.OrdinalIgnoreCase),
    };
  }

  // Unrecognized or missing values fall back to SelfSigned, so existing
  // Milestone 1 config files with no [Auth] section keep working unchanged.
  private static BedrockAuthMode ParseAuthMode(string? value) =>
      string.Equals(value, "Microsoft", StringComparison.OrdinalIgnoreCase)
          ? BedrockAuthMode.Microsoft
          : BedrockAuthMode.SelfSigned;
}
