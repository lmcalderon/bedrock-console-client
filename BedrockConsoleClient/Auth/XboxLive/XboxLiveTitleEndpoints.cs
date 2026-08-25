namespace BedrockConsoleClient.Auth.XboxLive;

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

// Resolves the relying party and signature policy an Xbox-Live-adjacent
// endpoint expects, by fetching Xbox Live's endpoint table (NSAL) and
// matching the request URL against it - the same lookup go-xsapi's
// nsal.Default/nsal.Current/TitleData.Match perform. Two tables exist:
// the generic "default" table (public, unauthenticated - covers SISU and
// other *.xboxlive.com services) and the title-specific "current" table
// (authenticated with a default-relying-party XSTS token - covers
// title-scoped services like PlayFab, confirmed necessary after the
// "default" table produced no match for playfabapi.com in a real run).
// Every other endpoint this project calls (device/XAST/XASU/XSTS, the
// Minecraft chain request) uses a fixed policy/relying party instead. Scoped
// down from go-xsapi's general-purpose matcher (no CIDR hosts, no
// per-endpoint certificates) since this project only ever resolves a
// handful of hosts.
internal static class XboxLiveTitleEndpoints
{
  private static readonly Uri DefaultEndpointsUrl = new("https://title.mgt.xboxlive.com/titles/default/endpoints?type=1");
  private static readonly Uri CurrentEndpointsUrl = new("https://title.mgt.xboxlive.com/titles/current/endpoints");

  public static async Task<XboxLiveSignaturePolicy> ResolvePolicyAsync(HttpClient client, Uri requestUri, CancellationToken ct) =>
      (await ResolveAsync(client, requestUri, ct)).Policy;

  public static async Task<(string RelyingParty, XboxLiveSignaturePolicy Policy)> ResolveAsync(HttpClient client, Uri requestUri, CancellationToken ct)
  {
    using var request = new HttpRequestMessage(HttpMethod.Get, DefaultEndpointsUrl);
    request.Headers.Add("x-xbl-contract-version", "1");

    TitleData data = await FetchAsync(client, request, ct);
    return Match(data, requestUri);
  }

  public static async Task<(string RelyingParty, XboxLiveSignaturePolicy Policy)> ResolveForCurrentTitleAsync(
      HttpClient client, ECDsa proofKey, XstsToken defaultRelyingPartyToken, Uri requestUri, CancellationToken ct)
  {
    using var request = new HttpRequestMessage(HttpMethod.Get, CurrentEndpointsUrl);
    request.Headers.Add("x-xbl-contract-version", "1");
    request.Headers.TryAddWithoutValidation("Authorization", defaultRelyingPartyToken.AuthorizationHeaderValue);
    XboxLiveRequestSigner.Sign(request, [], proofKey, XboxLiveSignaturePolicy.Default);

    TitleData data = await FetchAsync(client, request, ct);
    return Match(data, requestUri);
  }

  private static async Task<TitleData> FetchAsync(HttpClient client, HttpRequestMessage request, CancellationToken ct)
  {
    using HttpResponseMessage response = await client.SendAsync(request, ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<TitleData>(ct)
        ?? throw new XboxLiveAuthException("Xbox Live title endpoint response body was empty.");
  }

  private static (string RelyingParty, XboxLiveSignaturePolicy Policy) Match(TitleData data, Uri requestUri)
  {
    (string RelyingParty, XboxLiveSignaturePolicy Policy)? matched = null;
    foreach (Endpoint endpoint in data.Endpoints)
    {
      if (!Matches(endpoint, requestUri))
      {
        continue;
      }

      matched = (endpoint.RelyingParty!, ResolvePolicy(data, endpoint));
      if (endpoint.HostType == "fqdn")
      {
        break;
      }
    }

    return matched ?? throw new XboxLiveAuthException($"No NSAL title endpoint matched '{requestUri}'.");
  }

  private static XboxLiveSignaturePolicy ResolvePolicy(TitleData data, Endpoint endpoint)
  {
    if (endpoint.SignaturePolicyIndex is int index && index >= 0 && index < data.SignaturePolicies.Count)
    {
      SignaturePolicy policy = data.SignaturePolicies[index];
      return new XboxLiveSignaturePolicy(policy.Version, policy.MaxBodyBytes, policy.ExtraHeaders ?? []);
    }

    return XboxLiveSignaturePolicy.Default;
  }

  private static bool Matches(Endpoint endpoint, Uri requestUri)
  {
    if (string.IsNullOrEmpty(endpoint.RelyingParty) || endpoint.Protocol != requestUri.Scheme)
    {
      return false;
    }

    bool hostMatches = endpoint.HostType switch
    {
      "fqdn" => string.Equals(endpoint.Host, requestUri.Host, StringComparison.OrdinalIgnoreCase),
      "wildcard" => MatchesWildcard(endpoint.Host, requestUri.Host),
      _ => false,
    };
    if (!hostMatches)
    {
      return false;
    }

    if (endpoint.Port is int port && port != 0 && port != requestUri.Port)
    {
      return false;
    }

    return string.IsNullOrEmpty(endpoint.Path) || endpoint.Path == requestUri.AbsolutePath;
  }

  private static bool MatchesWildcard(string pattern, string host)
  {
    if (pattern.Length == 0 || pattern[0] != '*')
    {
      return false;
    }

    string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
    return Regex.IsMatch(host, regexPattern, RegexOptions.IgnoreCase);
  }

  private sealed record TitleData(
      [property: JsonPropertyName("EndPoints")] IReadOnlyList<Endpoint> Endpoints,
      IReadOnlyList<SignaturePolicy> SignaturePolicies);

  private sealed record Endpoint(
      string Protocol,
      string Host,
      int? Port,
      string HostType,
      string? Path,
      string? RelyingParty,
      int? SignaturePolicyIndex);

  private sealed record SignaturePolicy(uint Version, int MaxBodyBytes, IReadOnlyList<string>? ExtraHeaders);
}
