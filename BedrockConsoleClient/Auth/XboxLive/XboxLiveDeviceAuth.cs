namespace BedrockConsoleClient.Auth.XboxLive;

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

// XASD (Xbox Authentication Services for Device): identifies this device to
// Xbox Live and is a prerequisite for both the SISU authorize call and a
// direct XSTS request. Ported from go-xsapi's xal/xasd/authenticate.go.
internal static class XboxLiveDeviceAuth
{
  private static readonly Uri Endpoint = new("https://device.auth.xboxlive.com/device/authenticate");

  public static async Task<XboxLiveDeviceToken> AuthenticateAsync(HttpClient client, ECDsa proofKey, CancellationToken ct)
  {
    var body = new
    {
      RelyingParty = "http://auth.xboxlive.com",
      TokenType = "JWT",
      Properties = new
      {
        AuthMethod = "ProofOfPossession",
        Id = "{" + Guid.NewGuid().ToString() + "}",
        DeviceType = BedrockXboxLiveConfig.DeviceType,
        Version = BedrockXboxLiveConfig.DeviceVersion,
        ProofKey = XboxLiveProofKey.ToJsonWebKey(proofKey),
      },
    };
    byte[] bodyBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(body);

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
      throw new XboxLiveAuthException($"Xbox Live device authentication failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    Response? parsed = await response.Content.ReadFromJsonAsync<Response>(ct);
    if (parsed is null || string.IsNullOrEmpty(parsed.Token))
    {
      throw new XboxLiveAuthException("Xbox Live device authentication response was missing a token.");
    }

    return new XboxLiveDeviceToken(parsed.Token, parsed.NotAfter);
  }

  private sealed record Response(
      [property: JsonPropertyName("Token")] string Token,
      [property: JsonPropertyName("NotAfter")] DateTimeOffset NotAfter);
}
