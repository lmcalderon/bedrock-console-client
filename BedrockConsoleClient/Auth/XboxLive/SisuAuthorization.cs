namespace BedrockConsoleClient.Auth.XboxLive;

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

// The SISU authorize call: exchanges a Microsoft OAuth access token and a
// device token for a title token, a user token, and an XSTS token scoped to
// the default relying party ("http://xboxlive.com"), in one request. Ported
// from go-xsapi's xal/sisu/session.go authorize(). Unlike the device/XSTS
// token requests, this endpoint's signature policy is resolved dynamically
// via NSAL (see XboxLiveTitleEndpoints) rather than the fixed default, and -
// confirmed from source, not an oversight here - the request sets no
// Content-Type header.
internal static class SisuAuthorization
{
  private static readonly Uri Endpoint = new("https://sisu.xboxlive.com/authorize");

  public static async Task<SisuAuthorizationResult> AuthorizeAsync(
      HttpClient client,
      ECDsa proofKey,
      XboxLiveDeviceToken device,
      string microsoftAccessToken,
      CancellationToken ct)
  {
    var body = new
    {
      AccessToken = "t=" + microsoftAccessToken,
      AppId = BedrockXboxLiveConfig.ClientId,
      DeviceToken = device.Token,
      ProofKey = XboxLiveProofKey.ToJsonWebKey(proofKey),
      RelyingParty = BedrockXboxLiveConfig.DefaultRelyingParty,
      Sandbox = BedrockXboxLiveConfig.Sandbox,
      SiteName = "user.auth.xboxlive.com",
      UseModernGamertag = true,
    };
    byte[] bodyBytes = JsonSerializer.SerializeToUtf8Bytes(body);

    XboxLiveSignaturePolicy policy = await XboxLiveTitleEndpoints.ResolvePolicyAsync(client, Endpoint, ct);

    using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
    {
      Content = new ByteArrayContent(bodyBytes),
    };
    request.Headers.Add("User-Agent", BedrockXboxLiveConfig.UserAgent);
    XboxLiveRequestSigner.Sign(request, bodyBytes, proofKey, policy);

    using HttpResponseMessage response = await client.SendAsync(request, ct);
    if (!response.IsSuccessStatusCode)
    {
      throw new XboxLiveAuthException($"SISU authorization failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    Response? parsed = await response.Content.ReadFromJsonAsync<Response>(ct);
    if (parsed is null)
    {
      throw new XboxLiveAuthException("SISU authorization response body was empty.");
    }

    return new SisuAuthorizationResult(
        new XboxLiveTitleToken(parsed.TitleToken.Token, parsed.TitleToken.NotAfter),
        new XboxLiveUserToken(parsed.UserToken.Token, parsed.UserToken.NotAfter),
        ToXstsToken(parsed.AuthorizationToken));
  }

  private static XstsToken ToXstsToken(TokenResponse token)
  {
    UserInfo user = token.DisplayClaims.UserInfo[0];
    return new XstsToken(token.Token, user.UserHash, token.NotAfter, user.GamerTag, user.Xuid);
  }

  private sealed record Response(
      [property: JsonPropertyName("TitleToken")] TokenResponse TitleToken,
      [property: JsonPropertyName("UserToken")] TokenResponse UserToken,
      [property: JsonPropertyName("AuthorizationToken")] TokenResponse AuthorizationToken);

  private sealed record TokenResponse(
      [property: JsonPropertyName("Token")] string Token,
      [property: JsonPropertyName("NotAfter")] DateTimeOffset NotAfter,
      [property: JsonPropertyName("DisplayClaims")] DisplayClaims DisplayClaims);

  private sealed record DisplayClaims([property: JsonPropertyName("xui")] IReadOnlyList<UserInfo> UserInfo);

  private sealed record UserInfo(
      [property: JsonPropertyName("uhs")] string UserHash,
      [property: JsonPropertyName("gtg")] string? GamerTag,
      [property: JsonPropertyName("xid")] string? Xuid);
}
