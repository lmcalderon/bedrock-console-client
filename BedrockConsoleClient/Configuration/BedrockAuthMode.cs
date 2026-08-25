namespace BedrockConsoleClient.Configuration;

public enum BedrockAuthMode
{
  // A throwaway, self-signed identity - no Microsoft account needed. Only
  // works against offline-mode servers (xbox-auth=false).
  SelfSigned,

  // Signs in with a real Microsoft account via Xbox Live. Needed for
  // online-mode servers.
  Microsoft,
}
