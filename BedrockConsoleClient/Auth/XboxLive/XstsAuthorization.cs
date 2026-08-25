namespace BedrockConsoleClient.Auth.XboxLive;

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

// Requests an XSTS token for a specific relying party, given the device,
// title, and user tokens from a completed SISU authorize. This project only
// ever requests one relying party beyond SISU's default
// ("https://multiplayer.minecraft.net/", for the identity chain request).
// Ported from go-xsapi's xal/xsts/authorize.go; uses the fixed
// XboxLiveSignaturePolicy.Default, unlike SISU authorize.
internal static class XstsAuthorization
{
  private static readonly Uri Endpoint = new("https://xsts.auth.xboxlive.com/xsts/authorize");

  public static async Task<XstsToken> AuthorizeAsync(
      HttpClient client,
      ECDsa proofKey,
      XboxLiveDeviceToken device,
      XboxLiveTitleToken title,
      XboxLiveUserToken user,
      string relyingParty,
      CancellationToken ct)
  {
    var body = new
    {
      RelyingParty = relyingParty,
      TokenType = "JWT",
      Properties = new
      {
        SandboxId = BedrockXboxLiveConfig.Sandbox,
        DeviceToken = device.Token,
        TitleToken = title.Token,
        UserTokens = new[] { user.Token },
      },
    };
    byte[] bodyBytes = JsonSerializer.SerializeToUtf8Bytes(body);

    using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
    {
      Content = new ByteArrayContent(bodyBytes),
    };
    request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
    request.Headers.Add("User-Agent", BedrockXboxLiveConfig.UserAgent);
    request.Headers.Add("x-xbl-contract-version", "1");
    XboxLiveRequestSigner.Sign(request, bodyBytes, proofKey, XboxLiveSignaturePolicy.Default);

    using HttpResponseMessage response = await client.SendAsync(request, ct);
    if (!response.IsSuccessStatusCode)
    {
      throw new XboxLiveAuthException($"XSTS authorization for '{relyingParty}' failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    Response? parsed = await response.Content.ReadFromJsonAsync<Response>(ct);
    if (parsed is null || parsed.DisplayClaims.UserInfo.Count == 0)
    {
      throw new XboxLiveAuthException($"XSTS authorization response for '{relyingParty}' was missing user claims.");
    }

    UserInfo claimedUser = parsed.DisplayClaims.UserInfo[0];
    return new XstsToken(parsed.Token, claimedUser.UserHash, parsed.NotAfter, claimedUser.GamerTag, claimedUser.Xuid);
  }

  private sealed record Response(
      [property: JsonPropertyName("Token")] string Token,
      [property: JsonPropertyName("NotAfter")] DateTimeOffset NotAfter,
      [property: JsonPropertyName("DisplayClaims")] DisplayClaims DisplayClaims);

  private sealed record DisplayClaims([property: JsonPropertyName("xui")] IReadOnlyList<UserInfo> UserInfo);

  private sealed record UserInfo(
      [property: JsonPropertyName("uhs")] string UserHash,
      [property: JsonPropertyName("gtg")] string? GamerTag,
      [property: JsonPropertyName("xid")] string? Xuid);
}
