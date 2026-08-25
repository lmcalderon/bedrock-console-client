namespace BedrockConsoleClient.Networking.RakNet;

public sealed record RakNetConnectionOptions
{
  // Current Bedrock RakNet protocol version, per docs/notes/bedrock-feasibility.md.
  public byte ProtocolVersion { get; init; } = 11;

  public TimeSpan HandshakeStepTimeout { get; init; } = TimeSpan.FromMilliseconds(500);

  public TimeSpan HandshakeOverallTimeout { get; init; } = TimeSpan.FromSeconds(10);

  public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(3);

  // Base interval before the first resend of an unacknowledged reliable datagram.
  // Doubles on each subsequent attempt, capped at MaxResendInterval. See
  // RakNetConnection.BackoffDelay and docs/notes/raknet-design.md.
  public TimeSpan ResendInterval { get; init; } = TimeSpan.FromMilliseconds(500);

  public TimeSpan MaxResendInterval { get; init; } = TimeSpan.FromSeconds(8);

  // Give up and disconnect once a reliable datagram has been resent this many
  // times without an ACK. Treated as an unreachable/dead peer, not a bug.
  public int MaxResendAttempts { get; init; } = 10;
}
