namespace BedrockConsoleClient.Auth.XboxLive;

using System.Net.Http.Json;
using System.Text.Json.Serialization;

// Looks up the "franchise" authorization service's base URI for the current
// client version. Ported from gophertunnel's minecraft/service/discovery.go.
internal static class MinecraftServiceDiscovery
{
  private static readonly Uri DiscoveryUrl = new(
      $"https://client.discovery.minecraft-services.net/api/v1.0/discovery/MinecraftPE/builds/{BedrockXboxLiveConfig.ClientVersion}");

  public static async Task<AuthorizationEnvironment> DiscoverAuthEnvironmentAsync(HttpClient client, CancellationToken ct)
  {
    using HttpResponseMessage response = await client.GetAsync(DiscoveryUrl, ct);
    if (!response.IsSuccessStatusCode)
    {
      throw new XboxLiveAuthException($"Minecraft service discovery failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    Envelope? parsed = await response.Content.ReadFromJsonAsync<Envelope>(ct);
    AuthEnvironmentJson? auth = parsed?.Result.ServiceEnvironments.GetValueOrDefault("auth")?.GetValueOrDefault("prod");
    if (auth is null)
    {
      throw new XboxLiveAuthException("Minecraft service discovery response was missing the 'auth' service's 'prod' environment.");
    }

    return new AuthorizationEnvironment(new Uri(auth.ServiceUri), auth.PlayFabTitleId);
  }

  private sealed record Envelope([property: JsonPropertyName("result")] DiscoveryResult Result);

  private sealed record DiscoveryResult(
      [property: JsonPropertyName("serviceEnvironments")] IReadOnlyDictionary<string, IReadOnlyDictionary<string, AuthEnvironmentJson>> ServiceEnvironments);

  private sealed record AuthEnvironmentJson(
      [property: JsonPropertyName("serviceUri")] string ServiceUri,
      [property: JsonPropertyName("playFabTitleId")] string PlayFabTitleId);
}
