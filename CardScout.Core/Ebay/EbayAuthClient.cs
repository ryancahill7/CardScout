// CardScout.Core/Ebay/EbayAuthClient.cs
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CardScout.Core.Ebay;

public class EbayAuthClient
{
    private readonly HttpClient _http;
    private readonly string _appId;
    private readonly string _certId;
    private string? _cachedToken;
    private DateTime _tokenExpiry;

    public EbayAuthClient(HttpClient http, string appId, string certId)
    {
        _http = http;
        _appId = appId;
        _certId = certId;
    }

    public async Task<string> GetTokenAsync()
    {
        if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry)
            return _cachedToken;

        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_appId}:{_certId}"));

        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://api.ebay.com/identity/v1/oauth2/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["scope"] = "https://api.ebay.com/oauth/api_scope"
        });

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        _cachedToken = doc.RootElement.GetProperty("access_token").GetString();
        var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();
        _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60); // refresh 1 min early

        return _cachedToken!;
    }
}