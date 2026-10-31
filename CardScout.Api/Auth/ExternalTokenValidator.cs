using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CardScout.Api.Auth;

// Who an external provider says the user is, after its token was verified
public record ExternalIdentity(string Provider, string Subject, string? Email, bool EmailVerified, string? DisplayName);

// Verifies ID tokens from OpenID Connect providers (Google, Apple, any OIDC SSO) against the
// provider's published signing keys (JWKS from its discovery document, cached and refreshed on
// key rotation), plus issuer, audience, expiry and, when given, nonce.
public class ExternalTokenValidator
{
    public const string GoogleAuthority = "https://accounts.google.com";
    public const string AppleAuthority = "https://appleid.apple.com";

    private readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _configs = new();
    private readonly JsonWebTokenHandler _handler = new();
    private readonly HttpClient _http;

    public ExternalTokenValidator(HttpClient http)
    {
        _http = http;
    }

    public Task<OpenIdConnectConfiguration> GetConfigurationAsync(string authority, CancellationToken ct = default) =>
        Manager(authority).GetConfigurationAsync(ct);

    private ConfigurationManager<OpenIdConnectConfiguration> Manager(string authority) =>
        _configs.GetOrAdd(authority.TrimEnd('/'), a => new ConfigurationManager<OpenIdConnectConfiguration>(
            a + "/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(_http) { RequireHttps = true }));

    // Returns the validated token, or throws SecurityTokenException with the reason
    public async Task<JsonWebToken> ValidateAsync(
        string idToken, string authority, IEnumerable<string> audiences, IEnumerable<string>? extraIssuers = null,
        string? expectedNonce = null, CancellationToken ct = default)
    {
        var audienceList = audiences.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        if (audienceList.Count == 0) throw new SecurityTokenInvalidAudienceException("No audiences configured for this provider.");

        var manager = Manager(authority);
        for (var attempt = 0; ; attempt++)
        {
            var config = await manager.GetConfigurationAsync(ct);
            var issuers = new List<string> { config.Issuer };
            if (extraIssuers != null) issuers.AddRange(extraIssuers);

            var result = await _handler.ValidateTokenAsync(idToken, new TokenValidationParameters
            {
                ValidIssuers = issuers,
                ValidAudiences = audienceList,
                IssuerSigningKeys = config.SigningKeys,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ClockSkew = TimeSpan.FromMinutes(2),
            });

            if (result.IsValid && result.SecurityToken is JsonWebToken jwt)
            {
                if (expectedNonce != null)
                {
                    jwt.TryGetPayloadValue<string>("nonce", out var nonce);
                    if (nonce == null || !TokenService.FixedTimeEquals(nonce, expectedNonce))
                        throw new SecurityTokenValidationException("Nonce mismatch.");
                }
                return jwt;
            }

            // The provider may have rotated keys since we cached them: refetch once
            if (attempt == 0 && result.Exception is SecurityTokenSignatureKeyNotFoundException)
            {
                manager.RequestRefresh();
                continue;
            }
            throw result.Exception as SecurityTokenException
                  ?? new SecurityTokenValidationException(result.Exception?.Message ?? "Invalid token.");
        }
    }

    // email_verified is a boolean for Google and most IdPs, but Apple sends the string "true"
    public static bool ClaimIsTrue(JsonWebToken jwt, string claim)
    {
        if (jwt.TryGetPayloadValue<bool>(claim, out var b)) return b;
        return jwt.TryGetPayloadValue<string>(claim, out var s) && string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static string? StringClaim(JsonWebToken jwt, string claim) =>
        jwt.TryGetPayloadValue<string>(claim, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    public static string Sha256Hex(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
