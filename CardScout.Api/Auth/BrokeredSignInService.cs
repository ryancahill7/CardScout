using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CardScout.Core.Data;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CardScout.Api.Auth;

// Browser sign-in that the API runs on the app's behalf (works in Expo Go: no native SDKs, and the
// provider secrets stay on the server):
//
//   app ── GET /api/auth/{p}/start?redirectUri=cardscout://auth&codeChallenge=S256(verifier) ──▶ API
//   API ── 302 ──▶ provider consent (state, nonce, PKCE) ── 302 ──▶ /api/auth/{p}/callback
//   API exchanges the code, finds/creates the user, 302 ──▶ cardscout://auth?code=<one-time code>
//   app ── POST /api/auth/{p}/complete { code, codeVerifier } ──▶ access + refresh tokens
//
// Tokens never appear in a URL; the one-time code is useless without the app's PKCE verifier.
public class BrokeredSignInService
{
    public static readonly string[] Providers = ["google", "ebay", "oidc"];

    private static readonly TimeSpan FlowLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(2);

    private readonly AuthFlowRepository _flows;
    private readonly AuthService _auth;
    private readonly ExternalTokenValidator _validator;
    private readonly HttpClient _http;
    private readonly AuthOptions _options;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly ILogger<BrokeredSignInService> _logger;

    public BrokeredSignInService(AuthFlowRepository flows, AuthService auth, ExternalTokenValidator validator, HttpClient http,
        IOptions<AuthOptions> options, IConfiguration config, IHostEnvironment env, ILogger<BrokeredSignInService> logger)
    {
        _flows = flows;
        _auth = auth;
        _validator = validator;
        _http = http;
        _options = options.Value;
        _config = config;
        _env = env;
        _logger = logger;
    }

    public bool IsEnabled(string provider) => provider switch
    {
        "google" => _options.Google.BrowserFlowEnabled,
        "ebay" => !string.IsNullOrWhiteSpace(_options.Ebay.RuName)
                  && !string.IsNullOrWhiteSpace(_config["Ebay:AppId"]) && !string.IsNullOrWhiteSpace(_config["Ebay:CertId"]),
        "oidc" => _options.Oidc.Enabled,
        _ => false,
    };

    // Only the app's own deep-link schemes may receive a code (Expo Go's exp:// in Development)
    public bool IsAllowedAppRedirect(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri) || uri.Length > 1000 || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return false;
        var schemes = _options.AppRedirectSchemes.AsEnumerable();
        if (_env.IsDevelopment()) schemes = schemes.Concat(["exp", "exps"]);
        return schemes.Contains(parsed.Scheme, StringComparer.OrdinalIgnoreCase) && string.IsNullOrEmpty(parsed.Fragment);
    }

    // Returns the provider URL to send the browser to, or an error message
    public async Task<(string? Url, string? Error)> StartAsync(
        string provider, string? appRedirectUri, string? codeChallenge, string? email, HttpRequest request)
    {
        if (!IsEnabled(provider)) return (null, "This sign-in option isn't available.");
        if (!IsAllowedAppRedirect(appRedirectUri)) return (null, "Invalid redirectUri.");
        if (codeChallenge is not { Length: 43 } || codeChallenge.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            return (null, "codeChallenge must be an S256 PKCE challenge (43 base64url characters).");

        string? loginHint = null;
        if (provider == "oidc" && _options.Oidc.Domains.Length > 0)
        {
            var normalized = PasswordPolicy.NormalizeEmail(email);
            var domain = normalized?.Split('@')[1];
            if (domain == null || !_options.Oidc.Domains.Contains(domain, StringComparer.OrdinalIgnoreCase))
                return (null, "SSO isn't set up for that email domain. Sign in another way.");
            loginHint = normalized;
        }

        await _flows.DeleteAbandonedAsync(TimeSpan.FromDays(1));

        var state = TokenService.NewOpaqueToken();
        var nonce = TokenService.NewOpaqueToken(16);
        var verifier = provider == "ebay" ? null : TokenService.NewOpaqueToken(32);
        await _flows.CreateAsync(TokenService.Hash(state), provider, appRedirectUri!, codeChallenge, verifier, nonce, FlowLifetime);

        var callback = CallbackUrl(provider, request);
        if (provider == "ebay")
        {
            var query = new Dictionary<string, string?>
            {
                ["client_id"] = _config["Ebay:AppId"],
                ["redirect_uri"] = _options.Ebay.RuName,
                ["response_type"] = "code",
                ["scope"] = "https://api.ebay.com/oauth/api_scope/commerce.identity.readonly",
                ["state"] = state,
            };
            return (QueryHelpers(EbayHost("auth") + "/oauth2/authorize", query), null);
        }

        var (authority, clientId, scopes) = OidcSettings(provider);
        var config = await _validator.GetConfigurationAsync(authority);
        var authQuery = new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = callback,
            ["response_type"] = "code",
            ["scope"] = scopes,
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = TokenService.PkceChallenge(verifier!),
            ["code_challenge_method"] = "S256",
            ["login_hint"] = loginHint,
            ["prompt"] = provider == "google" ? "select_account" : null,
        };
        return (QueryHelpers(config.AuthorizationEndpoint, authQuery), null);
    }

    // Returns where to send the browser next: the app's deep link with a one-time code or an error.
    // Null means the state was unknown/expired, so there's no trusted app URL to go back to.
    public async Task<string?> CallbackAsync(string provider, IQueryCollection query, HttpRequest request)
    {
        var state = query["state"].ToString();
        if (string.IsNullOrEmpty(state)) return null;
        var flow = await _flows.ClaimCallbackAsync(TokenService.Hash(state));
        if (flow == null || flow.Provider != provider) return null;

        string AppUrl(params (string Key, string Value)[] values)
        {
            var sep = flow.AppRedirectUri.Contains('?') ? '&' : '?';
            return flow.AppRedirectUri + sep + string.Join('&', values.Select(v => $"{v.Key}={Uri.EscapeDataString(v.Value)}"));
        }

        var code = query["code"].ToString();
        if (!string.IsNullOrEmpty(query["error"]) || string.IsNullOrEmpty(code))
        {
            var error = query["error"].ToString();
            return AppUrl(("error", error == "access_denied" || string.IsNullOrEmpty(error) ? "cancelled" : "provider_error"));
        }

        ExternalIdentity identity;
        try
        {
            identity = provider == "ebay"
                ? await ExchangeEbayAsync(code)
                : await ExchangeOidcAsync(provider, code, flow.ProviderVerifier!, flow.Nonce!, request);
        }
        catch (Exception ex) when (ex is SecurityTokenException or HttpRequestException or JsonException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "{Provider} sign-in callback failed", provider);
            return AppUrl(("error", "provider_error"));
        }

        var userId = await _auth.ResolveExternalUserAsync(identity);
        if (userId == null) return AppUrl(("error", "account_disabled"));

        var oneTimeCode = TokenService.NewOpaqueToken();
        await _flows.SetResultAsync(flow.Id, userId.Value, TokenService.Hash(oneTimeCode), CodeLifetime);
        return AppUrl(("code", oneTimeCode));
    }

    public async Task<AuthResponse?> CompleteAsync(CompleteSignInRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 200) return null;
        if (request.CodeVerifier is not { Length: >= 43 and <= 128 }) return null;

        var flow = await _flows.ConsumeCodeAsync(TokenService.Hash(request.Code));
        if (flow?.UserId == null) return null;
        if (!TokenService.FixedTimeEquals(TokenService.PkceChallenge(request.CodeVerifier), flow.AppCodeChallenge))
        {
            _logger.LogWarning("Sign-in code for user {UserId} presented with the wrong PKCE verifier", flow.UserId);
            return null;
        }
        return await _auth.IssueSessionAsync(flow.UserId.Value, flow.Provider);
    }

    private async Task<ExternalIdentity> ExchangeOidcAsync(string provider, string code, string verifier, string nonce, HttpRequest request)
    {
        var (authority, clientId, _) = OidcSettings(provider);
        var config = await _validator.GetConfigurationAsync(authority);
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = CallbackUrl(provider, request),
            ["client_id"] = clientId,
            ["code_verifier"] = verifier,
        };
        var secret = provider == "google" ? _options.Google.WebClientSecret : _options.Oidc.ClientSecret;
        if (!string.IsNullOrWhiteSpace(secret)) form["client_secret"] = secret;

        using var response = await _http.PostAsync(config.TokenEndpoint, new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token endpoint returned {(int)response.StatusCode}: {Truncate(body)}");

        using var doc = JsonDocument.Parse(body);
        var idToken = doc.RootElement.TryGetProperty("id_token", out var t) ? t.GetString() : null;
        if (string.IsNullOrEmpty(idToken)) throw new InvalidOperationException("Token response had no id_token.");

        var jwt = await _validator.ValidateAsync(idToken, authority, [clientId],
            provider == "google" ? ["accounts.google.com"] : null, expectedNonce: nonce);

        var name = ExternalTokenValidator.StringClaim(jwt, "name")
                   ?? string.Join(' ', new[] { ExternalTokenValidator.StringClaim(jwt, "given_name"), ExternalTokenValidator.StringClaim(jwt, "family_name") }
                       .Where(s => s != null)).NullIfEmpty();
        var email = ExternalTokenValidator.StringClaim(jwt, "email");
        var subject = provider == "oidc" ? $"{jwt.Issuer}#{jwt.Subject}" : jwt.Subject;
        return new ExternalIdentity(provider, subject, email, ExternalTokenValidator.ClaimIsTrue(jwt, "email_verified"), name);
    }

    // eBay isn't OpenID Connect: swap the code for a user token, then ask the Commerce Identity API who it is.
    // eBay doesn't say whether the email is verified, so eBay sign-ins never link to accounts by email.
    private async Task<ExternalIdentity> ExchangeEbayAsync(string code)
    {
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_config["Ebay:AppId"]}:{_config["Ebay:CertId"]}"));
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, EbayHost("api") + "/identity/v1/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = _options.Ebay.RuName!,
            }),
        };
        tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        using var tokenResponse = await _http.SendAsync(tokenRequest);
        var tokenBody = await tokenResponse.Content.ReadAsStringAsync();
        if (!tokenResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"eBay token exchange returned {(int)tokenResponse.StatusCode}: {Truncate(tokenBody)}");
        using var tokenDoc = JsonDocument.Parse(tokenBody);
        var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString();

        // The user token is only used for this one call and then dropped; nothing is stored
        using var userRequest = new HttpRequestMessage(HttpMethod.Get, EbayHost("apiz") + "/commerce/identity/v1/user/");
        userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var userResponse = await _http.SendAsync(userRequest);
        var userBody = await userResponse.Content.ReadAsStringAsync();
        if (!userResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"eBay getUser returned {(int)userResponse.StatusCode}: {Truncate(userBody)}");
        using var userDoc = JsonDocument.Parse(userBody);
        var userId = userDoc.RootElement.GetProperty("userId").GetString();
        if (string.IsNullOrEmpty(userId)) throw new InvalidOperationException("eBay getUser had no userId.");
        var username = userDoc.RootElement.TryGetProperty("username", out var u) ? u.GetString() : null;
        return new ExternalIdentity("ebay", userId, null, false, username);
    }

    private (string Authority, string ClientId, string Scopes) OidcSettings(string provider) => provider switch
    {
        "google" => (ExternalTokenValidator.GoogleAuthority, _options.Google.WebClientId!, "openid email profile"),
        "oidc" => (_options.Oidc.Authority!, _options.Oidc.ClientId!, _options.Oidc.Scopes),
        _ => throw new InvalidOperationException($"{provider} is not an OIDC provider"),
    };

    private string EbayHost(string prefix) =>
        string.Equals(_options.Ebay.Environment, "sandbox", StringComparison.OrdinalIgnoreCase)
            ? $"https://{prefix}.sandbox.ebay.com"
            : $"https://{prefix}.ebay.com";

    public string CallbackUrl(string provider, HttpRequest request)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.PublicBaseUrl)
            ? $"{request.Scheme}://{request.Host}"
            : _options.PublicBaseUrl.TrimEnd('/');
        return $"{baseUrl}/api/auth/{provider}/callback";
    }

    private static string QueryHelpers(string baseUrl, Dictionary<string, string?> query) =>
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(baseUrl,
            query.Where(kv => kv.Value != null).Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)));

    private static string Truncate(string s) => s.Length > 300 ? s[..300] : s;
}

internal static class StringExtensions
{
    public static string? NullIfEmpty(this string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
