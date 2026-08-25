namespace BedrockConsoleClient.Auth.XboxLive;

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BedrockConsoleClient.Networking.Bedrock.Identity;

// The "franchise" authorization service: exchanges a PlayFab session ticket
// for a session token, then that session token plus a connection's public
// key for a multiplayer token - the value that becomes the Login packet's
// AuthInfoJson.Token field (distinct from the identity chain, which becomes
// AuthInfoJson.Certificate). Ported from gophertunnel's
// minecraft/service/token.go AuthorizationEnvironment.Token/MultiplayerToken.
internal static class MinecraftAuthorizationService
{
  public static async Task<string> RequestSessionTokenAsync(HttpClient client, AuthorizationEnvironment env, string playFabSessionTicket, CancellationToken ct)
  {
    var body = new
    {
      device = new
      {
        applicationType = "MinecraftPE",
        capabilities = Array.Empty<string>(),
        gameVersion = BedrockXboxLiveConfig.ClientVersion,
        id = Guid.NewGuid().ToString(),

        // 16 GiB, gophertunnel's own default when no real value is known.
        memory = (16UL * (1UL << 30)).ToString(System.Globalization.CultureInfo.InvariantCulture),
        platform = "Windows10",
        playFabTitleId = env.PlayFabTitleId,
        storePlatform = "uwp.store",
        type = "Windows10",
      },
      user = new
      {
        token = playFabSessionTicket,
        tokenType = "PlayFab",
        language = "en",
        languageCode = "en-US",
        regionCode = "US",
      },
    };

    Uri endpoint = new(env.ServiceUri, "/api/v1.0/session/start");
    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
    {
      Content = new StringContent(JsonSerializer.Serialize(body)),
    };
    request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

    using HttpResponseMessage response = await client.SendAsync(request, ct);
    if (!response.IsSuccessStatusCode)
    {
      throw new XboxLiveAuthException($"Minecraft session token request failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    SessionEnvelope? parsed = await response.Content.ReadFromJsonAsync<SessionEnvelope>(ct);
    if (string.IsNullOrEmpty(parsed?.Result?.AuthorizationHeader))
    {
      throw new XboxLiveAuthException("Minecraft session token response was missing an authorization header.");
    }

    return parsed.Result.AuthorizationHeader;
  }

  public static async Task<string> RequestMultiplayerTokenAsync(HttpClient client, AuthorizationEnvironment env, string sessionAuthorizationHeader, BedrockKeyPair keyPair, CancellationToken ct)
  {
    Uri endpoint = new(env.ServiceUri, "/api/v1.0/multiplayer/session/start");
    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
    {
      Content = new StringContent(JsonSerializer.Serialize(new { publicKey = keyPair.PublicKeyBase64Der })),
    };
    request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
    request.Headers.TryAddWithoutValidation("Authorization", sessionAuthorizationHeader);

    using HttpResponseMessage response = await client.SendAsync(request, ct);
    if (!response.IsSuccessStatusCode)
    {
      throw new XboxLiveAuthException($"Minecraft multiplayer token request failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    MultiplayerEnvelope? parsed = await response.Content.ReadFromJsonAsync<MultiplayerEnvelope>(ct);
    if (string.IsNullOrEmpty(parsed?.Result?.SignedToken))
    {
      throw new XboxLiveAuthException("Minecraft multiplayer token response was missing a signed token.");
    }

    return parsed.Result.SignedToken;
  }

  private sealed record SessionEnvelope([property: JsonPropertyName("result")] SessionResult? Result);

  private sealed record SessionResult([property: JsonPropertyName("authorizationHeader")] string AuthorizationHeader);

  private sealed record MultiplayerEnvelope([property: JsonPropertyName("result")] MultiplayerResult? Result);

  private sealed record MultiplayerResult([property: JsonPropertyName("signedToken")] string SignedToken);
}
