namespace BedrockConsoleClient.Networking.RakNet.Reliability;

internal static class FrameReliabilityExtensions
{
  public static bool IsReliable(this FrameReliability reliability) =>
      reliability is FrameReliability.Reliable or FrameReliability.ReliableOrdered or FrameReliability.ReliableSequenced;

  public static bool IsSequenced(this FrameReliability reliability) =>
      reliability is FrameReliability.UnreliableSequenced or FrameReliability.ReliableSequenced;

  public static bool IsSequencedOrOrdered(this FrameReliability reliability) =>
      reliability.IsSequenced() || reliability == FrameReliability.ReliableOrdered;
}
