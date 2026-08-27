namespace BedrockConsoleClient.Networking.RakNet;

/// <summary>
/// A Bedrock server's advertised MOTD, as parsed from a RakNet
/// unconnected-pong reply. Field order/meaning follows the de facto
/// semicolon-delimited MOTD format Bedrock servers use; trailing/unrecognized
/// fields are preserved in <see cref="Raw"/> but not
/// individually parsed.
/// </summary>
public sealed record RakNetServerInfo(
    string Edition,
    string MotdLine1,
    int ProtocolVersion,
    string GameVersion,
    int PlayerCount,
    int MaxPlayerCount,
    string MotdLine2,
    string GameMode,
    string Raw)
{
  public static RakNetServerInfo Parse(string motd)
  {
    string[] parts = motd.Split(';');
    string Field(int index) => index < parts.Length ? parts[index] : string.Empty;
    int IntField(int index) => int.TryParse(Field(index), out int value) ? value : 0;

    return new RakNetServerInfo(
        Edition: Field(0),
        MotdLine1: Field(1),
        ProtocolVersion: IntField(2),
        GameVersion: Field(3),
        PlayerCount: IntField(4),
        MaxPlayerCount: IntField(5),
        MotdLine2: Field(7),
        GameMode: Field(8),
        Raw: motd);
  }
}
