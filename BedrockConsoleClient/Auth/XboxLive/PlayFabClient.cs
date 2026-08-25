namespace BedrockConsoleClient.Auth.XboxLive;

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

// Logs in to Minecraft's PlayFab title with an Xbox Live identity, producing
// a PlayFab session ticket - the credential the franchise authorization
// service (MinecraftAuthorizationService) needs. This is the piece the
// milestone's first (WebFetch-based) research draft had right and a later,
// overcorrected simplification wrongly deleted: PlayFab isn't NetherNet-only
// - the multiplayer token it feeds into is embedded in the Login packet
// itself, over any transport. Ported from go-playfab's xbox.go/login.go.
// Unlike the Xbox Live token requests, PlayFab login isn't request-signed;
// the XSTS token is simply carried in the request body's XboxToken field.
internal static class PlayFabClient
{
  public static async Task<string> LoginWithXboxAsync(
      HttpClient client,
      ECDsa proofKey,
      XboxLiveDeviceToken device,
      XboxLiveTitleToken title,
      XboxLiveUserToken user,
      XstsToken defaultRelyingPartyToken,
      CancellationToken ct)
  {
    Uri endpoint = new($"https://{BedrockXboxLiveConfig.PlayFabTitleId.ToLowerInvariant()}.playfabapi.com/Client/LoginWithXbox");

    // PlayFab is a title-specific service, not one of the generic
    // *.xboxlive.com endpoints - its relying party only appears in the
    // "current title" NSAL table, not the public "default" one (confirmed
    // after the default table produced no match in a real run).
    (string relyingParty, _) = await XboxLiveTitleEndpoints.ResolveForCurrentTitleAsync(client, proofKey, defaultRelyingPartyToken, endpoint, ct);
    XstsToken xstsToken = await XstsAuthorization.AuthorizeAsync(client, proofKey, device, title, user, relyingParty, ct);

    var body = new
    {
      TitleId = BedrockXboxLiveConfig.PlayFabTitleId,
      XboxToken = xstsToken.AuthorizationHeaderValue,
      CreateAccount = true,
    };

    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
    {
      Content = new StringContent(JsonSerializer.Serialize(body)),
    };
    request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

    using HttpResponseMessage response = await client.SendAsync(request, ct);
    if (!response.IsSuccessStatusCode)
    {
      throw new XboxLiveAuthException($"PlayFab login failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    Envelope? parsed = await response.Content.ReadFromJsonAsync<Envelope>(ct);
    if (string.IsNullOrEmpty(parsed?.Result?.SessionTicket))
    {
      throw new XboxLiveAuthException("PlayFab login response was missing a session ticket.");
    }

    return parsed.Result.SessionTicket;
  }

  private sealed record Envelope([property: JsonPropertyName("data")] LoginResult? Result);

  private sealed record LoginResult([property: JsonPropertyName("SessionTicket")] string SessionTicket);
}
