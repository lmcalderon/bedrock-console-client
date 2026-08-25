namespace BedrockConsoleClient.Configuration;

// Minimal hand-rolled INI reader: [Section] headers, key=value lines,
// '#'/';' comment lines. No external dependency and no JSON/YAML on
// purpose: this project's target audience already knows this exact
// format from server.properties and MCC's MinecraftClient.ini.
internal static class IniFile
{
  public static Dictionary<string, Dictionary<string, string>> Parse(string path)
  {
    var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
    string currentSection = string.Empty;
    sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    foreach (string rawLine in File.ReadLines(path))
    {
      string line = rawLine.Trim();
      if (line.Length == 0 || line[0] is '#' or ';')
      {
        continue;
      }

      if (line[0] == '[' && line[^1] == ']')
      {
        currentSection = line[1..^1].Trim();
        if (!sections.ContainsKey(currentSection))
        {
          sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        continue;
      }

      int separatorIndex = line.IndexOf('=');
      if (separatorIndex < 0)
      {
        continue;
      }

      string key = line[..separatorIndex].Trim();
      string value = line[(separatorIndex + 1)..].Trim();
      sections[currentSection][key] = value;
    }

    return sections;
  }
}
