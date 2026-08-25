namespace BedrockConsoleClient.Auth.XboxLive;

using System.Text.Json;
using BedrockConsoleClient.Networking.Bedrock.Identity;

// The last hop: exchanges the Minecraft-relying-party XSTS token and the
// connection's own P-384 public key for the identity chain JWT the Login
// packet carries. Ported from gophertunnel's minecraft/auth/minecraft.go
// RequestMinecraftChain. Unlike every earlier call in this flow, this one
// omits the Signature header entirely (confirmed from source: the reference
// client explicitly strips it here) - only the bearer Authorization header
// from the XSTS token is required.
internal static class MinecraftChainClient
{
  private static readonly Uri Endpoint = new("https://multiplayer.minecraft.net/authentication");

  public static async Task<string> RequestChainAsync(HttpClient client, XstsToken minecraftToken, BedrockKeyPair keyPair, CancellationToken ct)
  {
    string body = JsonSerializer.Serialize(new { identityPublicKey = keyPair.PublicKeyBase64Der });

    using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
    {
      Content = new StringContent(body),
    };
    request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
    request.Headers.Add("User-Agent", "MCPE/Android");
    request.Headers.Add("Client-Version", BedrockXboxLiveConfig.ClientVersion);
    request.Headers.TryAddWithoutValidation("Authorization", minecraftToken.AuthorizationHeaderValue);

    using HttpResponseMessage response = await client.SendAsync(request, ct);
    string responseBody = await response.Content.ReadAsStringAsync(ct);
    if (!response.IsSuccessStatusCode)
    {
      throw new XboxLiveAuthException($"Minecraft identity chain request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {responseBody}");
    }

    return responseBody;
  }
}
