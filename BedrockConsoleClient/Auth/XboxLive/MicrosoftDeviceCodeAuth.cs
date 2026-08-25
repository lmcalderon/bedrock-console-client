namespace BedrockConsoleClient.Auth.XboxLive;

using System.Net.Http.Json;
using System.Text.Json.Serialization;

// The initial Microsoft sign-in step: a standard RFC 8628 OAuth2
// device-code flow (print a URL and a short code, poll until the user
// finishes signing in elsewhere) against the older Xbox/Live Connect
// endpoints (login.live.com), not the modern login.microsoftonline.com
// endpoints MCC's Java-edition client uses. Confirmed from go-xsapi's
// xal/sisu/oauth2.go (microsoft.LiveConnectEndpoint, device auth URL
// overridden to oauth20_connect.srf) and golang.org/x/oauth2's RFC 8628
// implementation, not assumed from MCC's flow.
internal static class MicrosoftDeviceCodeAuth
{
  private static readonly Uri DeviceAuthUrl = new("https://login.live.com/oauth20_connect.srf");
  private static readonly Uri TokenUrl = new("https://login.live.com/oauth20_token.srf");

  public static async Task<MicrosoftDeviceCodeInfo> RequestDeviceCodeAsync(HttpClient client, CancellationToken ct)
  {
    var form = new Dictionary<string, string>
    {
      ["client_id"] = BedrockXboxLiveConfig.ClientId,
      ["scope"] = BedrockXboxLiveConfig.OAuthScope,
      ["response_type"] = "device_code",
    };

    using HttpResponseMessage response = await client.PostAsync(DeviceAuthUrl, new FormUrlEncodedContent(form), ct);
    if (!response.IsSuccessStatusCode)
    {
      throw new XboxLiveAuthException($"Requesting a Microsoft device code failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    DeviceCodeResponse? parsed = await response.Content.ReadFromJsonAsync<DeviceCodeResponse>(ct);
    if (parsed is null)
    {
      throw new XboxLiveAuthException("Microsoft device code response body was empty.");
    }

    return new MicrosoftDeviceCodeInfo(
        parsed.DeviceCode,
        parsed.UserCode,
        parsed.VerificationUri,
        DateTimeOffset.UtcNow.AddSeconds(parsed.ExpiresIn),
        TimeSpan.FromSeconds(parsed.Interval == 0 ? 5 : parsed.Interval));
  }

  public static async Task<MicrosoftTokenResult> PollForAccessTokenAsync(HttpClient client, MicrosoftDeviceCodeInfo deviceCode, CancellationToken ct)
  {
    TimeSpan interval = deviceCode.PollInterval;
    while (true)
    {
      if (DateTimeOffset.UtcNow >= deviceCode.ExpiresAt)
      {
        throw new XboxLiveAuthException("Microsoft device code expired before sign-in was completed.");
      }

      await Task.Delay(interval, ct);

      var form = new Dictionary<string, string>
      {
        ["client_id"] = BedrockXboxLiveConfig.ClientId,
        ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
        ["device_code"] = deviceCode.DeviceCode,
        ["scope"] = BedrockXboxLiveConfig.OAuthScope,
      };

      using HttpResponseMessage response = await client.PostAsync(TokenUrl, new FormUrlEncodedContent(form), ct);
      TokenResponse? parsed = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);

      if (response.IsSuccessStatusCode && parsed is not null && parsed.AccessToken is not null)
      {
        return ToResult(parsed);
      }

      switch (parsed?.Error)
      {
        case "authorization_pending":
          continue;
        case "slow_down":
          interval += TimeSpan.FromSeconds(5);
          continue;
        default:
          throw new XboxLiveAuthException($"Microsoft device code sign-in failed: {parsed?.Error ?? response.ReasonPhrase}.");
      }
    }
  }

  public static async Task<MicrosoftTokenResult> RefreshAsync(HttpClient client, string refreshToken, CancellationToken ct)
  {
    var form = new Dictionary<string, string>
    {
      ["client_id"] = BedrockXboxLiveConfig.ClientId,
      ["grant_type"] = "refresh_token",
      ["refresh_token"] = refreshToken,
      ["scope"] = BedrockXboxLiveConfig.OAuthScope,
    };

    using HttpResponseMessage response = await client.PostAsync(TokenUrl, new FormUrlEncodedContent(form), ct);
    if (!response.IsSuccessStatusCode)
    {
      throw new XboxLiveAuthException($"Refreshing the Microsoft sign-in failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    TokenResponse? parsed = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
    if (parsed?.AccessToken is null)
    {
      throw new XboxLiveAuthException("Microsoft token refresh response was missing an access token.");
    }

    return ToResult(parsed);
  }

  private static MicrosoftTokenResult ToResult(TokenResponse parsed) => new(
      parsed.AccessToken!,
      parsed.RefreshToken ?? string.Empty,
      DateTimeOffset.UtcNow.AddSeconds(parsed.ExpiresIn));

  private sealed record DeviceCodeResponse(
      [property: JsonPropertyName("device_code")] string DeviceCode,
      [property: JsonPropertyName("user_code")] string UserCode,
      [property: JsonPropertyName("verification_uri")] string VerificationUri,
      [property: JsonPropertyName("expires_in")] int ExpiresIn,
      [property: JsonPropertyName("interval")] int Interval);

  private sealed record TokenResponse(
      [property: JsonPropertyName("access_token")] string? AccessToken,
      [property: JsonPropertyName("refresh_token")] string? RefreshToken,
      [property: JsonPropertyName("expires_in")] int ExpiresIn,
      [property: JsonPropertyName("error")] string? Error);
}
