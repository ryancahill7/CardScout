using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CardScout.Api.Auth;

// Access tokens: short-lived HS256 JWTs signed with Auth:JwtSigningKey.
// Opaque tokens (refresh, reset, one-time codes): 32 random bytes, base64url; only SHA-256 hashes are stored.
public class TokenService
{
    private readonly AuthOptions _options;
    private readonly SymmetricSecurityKey _key;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly TokenValidationParameters _validation;

    public TokenService(IOptions<AuthOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.JwtSigningKey))
            throw new InvalidOperationException(
                "Auth:JwtSigningKey is not set. Generate one and store it with `dotnet user-secrets set \"Auth:JwtSigningKey\" <base64> --project CardScout.Api`.");

        byte[] keyBytes;
        try { keyBytes = Convert.FromBase64String(_options.JwtSigningKey); }
        catch (FormatException) { throw new InvalidOperationException("Auth:JwtSigningKey must be base64."); }
        if (keyBytes.Length < 32) throw new InvalidOperationException("Auth:JwtSigningKey must be at least 32 bytes.");

        _key = new SymmetricSecurityKey(keyBytes);
        _validation = new TokenValidationParameters
        {
            ValidIssuer = _options.Issuer,
            ValidAudience = _options.Audience,
            IssuerSigningKey = _key,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    }

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(_options.AccessTokenMinutes);
    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshTokenDays);

    public (string Token, DateTime ExpiresAt) CreateAccessToken(long userId, string sessionId, string authMethod)
    {
        var now = DateTime.UtcNow;
        var expires = now.Add(AccessTokenLifetime);
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, NewOpaqueToken(16)),
                new Claim(JwtRegisteredClaimNames.Sid, sessionId),
                new Claim("amr", authMethod),
            ]),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256),
        });
        return (token, expires);
    }

    // The user id from a valid access token, or null
    public async Task<(long UserId, string? AuthMethod)?> ValidateAccessTokenAsync(string token)
    {
        var result = await _handler.ValidateTokenAsync(token, _validation);
        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt) return null;
        if (!long.TryParse(jwt.Subject, out var userId)) return null;
        jwt.TryGetPayloadValue<string>("amr", out var amr);
        return (userId, amr);
    }

    public static string NewOpaqueToken(int bytes = 32) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    // RFC 7636 S256: BASE64URL(SHA256(ASCII(verifier)))
    public static string PkceChallenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
