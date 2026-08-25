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

    return new BedrockClientConfig
    {
      ServerAddress = main.GetValueOrDefault("ServerAddress", "127.0.0.1:19132"),
      Username = main.GetValueOrDefault("Username", "BedrockClient"),
    };
  }
}
