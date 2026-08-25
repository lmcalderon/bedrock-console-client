namespace BedrockConsoleClient.Auth.XboxLive;

internal sealed class XboxLiveAuthException : Exception
{
  public XboxLiveAuthException(string message)
      : base(message)
  {
  }

  public XboxLiveAuthException(string message, Exception innerException)
      : base(message, innerException)
  {
  }
}
