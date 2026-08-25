using System.Globalization;
using System.Net;
using BedrockConsoleClient.Auth.XboxLive;
using BedrockConsoleClient.Configuration;
using BedrockConsoleClient.Networking.Bedrock;
using BedrockConsoleClient.Networking.Bedrock.Identity;
using BedrockConsoleClient.Networking.RakNet;

Log("Bedrock Console Client");

var config = BedrockClientConfigLoader.LoadOrCreateDefault();
Log($"Config: ServerAddress={config.ServerAddress}, Username={config.Username}, AuthMode={config.AuthMode} ({BedrockClientConfigLoader.FileName})");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
  e.Cancel = true;
  cts.Cancel();
};

// Xbox Live sign-in (when configured) happens before the RakNet connection
// opens: interactive device-code sign-in can take far longer than
// BedrockLoginOptions.LoginTimeout allows for the handshake that follows.
using var authHttpClient = new HttpClient();
IIdentityChainProvider identityProvider;
try
{
  identityProvider = config.AuthMode == BedrockAuthMode.Microsoft
      ? await XboxLiveIdentityChainProvider.SignInAsync(authHttpClient, message => Log($"[auth] {message}"), cts.Token)
      : new SelfSignedIdentityChainProvider(config.Username);
}
catch (Exception ex)
{
  Log($"Xbox Live sign-in failed: {ex.Message}");
  return;
}

IPEndPoint endpoint;
try
{
  endpoint = await ServerEndpointResolver.ResolveAsync(config.ServerAddress, cts.Token);
}
catch (Exception ex)
{
  Log($"Invalid ServerAddress in {BedrockClientConfigLoader.FileName}: {ex.Message}");
  return;
}

Log($"Connecting to {endpoint} ...");
RakNetConnection connection;
try
{
  connection = await RakNetClient.ConnectAsync(
      endpoint,
      onStateChanged: state =>
      {
        Log($"[raknet] {state}");
        if (state == ConnectionState.Disconnected)
        {
          // The server can end the session on its own. Wake the idle loop
          // below so the process exits instead of hanging until Ctrl+C.
          cts.Cancel();
        }
      },
      ct: cts.Token);
}
catch (Exception ex)
{
  Log($"RakNet connect failed: {ex.Message}");
  return;
}

connection.PingRoundTripMeasured += rtt => Log($"[ping] round trip {rtt.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)} ms");
connection.ConnectionLost += reason => Log($"[error] connection lost: {reason}");

Log("RakNet connected. Starting Bedrock login...");
var loginOptions = new BedrockLoginOptions
{
  Username = config.Username,
  ServerAddress = endpoint.ToString(),
};

BedrockSession session;
try
{
  session = await BedrockSession.LoginAsync(
      connection,
      loginOptions,
      identityProvider,
      onStateChanged: state => Log($"[login] {state}"),
      ct: cts.Token);
}
catch (Exception ex)
{
  Log($"Bedrock login failed: {ex.Message}");
  await connection.DisconnectAsync();
  return;
}

Log("Spawned. Idling; press Ctrl+C to disconnect.");
try
{
  await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
}

Log("Disconnecting...");
await session.DisposeAsync();
Log("Shut down.");

// Timestamps make it possible to correlate this log against the server's own
// (also HH:mm:ss.fff) console output when diagnosing handshake/timeout issues.
static void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
