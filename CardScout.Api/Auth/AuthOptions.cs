namespace CardScout.Api.Auth;

// Bound from the "Auth" config section. Secrets (JwtSigningKey, client secrets) live in user-secrets.
// A provider whose settings are missing is simply turned off and hidden from GET /api/auth/providers.
public class AuthOptions
{
    // Base64 of at least 32 random bytes (HMAC-SHA256 key for access tokens). Required.
    public string? JwtSigningKey { get; set; }
    public string Issuer { get; set; } = "cardscout-api";
    public string Audience { get; set; } = "cardscout-app";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 60;

    // Public https base URL of this API (e.g. a tunnel or the deployed host). Used to build the
    // provider callback URLs for brokered sign-in. Falls back to the request's own host.
    public string? PublicBaseUrl { get; set; }

    // Deep-link schemes the API may send a one-time sign-in code back to. "exp"/"exps" (Expo Go)
    // are also allowed in Development.
    public string[] AppRedirectSchemes { get; set; } = ["cardscout"];

    // Where the password reset email points; the token is appended as ?token=
    public string PasswordResetUrl { get; set; } = "cardscout://reset-password";
    public int PasswordResetMinutes { get; set; } = 30;

    public int MaxFailedLogins { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;

    public GoogleOptions Google { get; set; } = new();
    public AppleOptions Apple { get; set; } = new();
    public EbayOptions Ebay { get; set; } = new();
    public OidcOptions Oidc { get; set; } = new();
}

public class GoogleOptions
{
    // Audiences accepted for ID tokens posted to /api/auth/google (iOS/Android/web client ids)
    public string[] ClientIds { get; set; } = [];
    // Web application client used for the browser flow the app runs in Expo Go (needs the secret)
    public string? WebClientId { get; set; }
    public string? WebClientSecret { get; set; }

    public bool BrowserFlowEnabled => !string.IsNullOrWhiteSpace(WebClientId) && !string.IsNullOrWhiteSpace(WebClientSecret);
    public IEnumerable<string> Audiences =>
        ClientIds.Concat(string.IsNullOrWhiteSpace(WebClientId) ? [] : [WebClientId!]).Where(s => !string.IsNullOrWhiteSpace(s));
}

public class AppleOptions
{
    // The app's bundle id(s) / Services ID. Expo Go signs in as "host.exp.Exponent".
    public string[] Audiences { get; set; } = [];
    public bool Enabled => Audiences.Any(a => !string.IsNullOrWhiteSpace(a));
}

public class EbayOptions
{
    // The RuName (eBay redirect URL name) from the eBay developer portal; its "auth accepted URL"
    // must be {PublicBaseUrl}/api/auth/ebay/callback. App id / cert id come from Ebay:AppId/CertId.
    public string? RuName { get; set; }
    // "production" or "sandbox"
    public string Environment { get; set; } = "production";
}

public class OidcOptions
{
    // e.g. https://login.microsoftonline.com/{tenant}/v2.0 or https://your-org.okta.com
    public string? Authority { get; set; }
    public string? ClientId { get; set; }
    // Optional: confidential clients send it at the token endpoint (PKCE is always used)
    public string? ClientSecret { get; set; }
    public string DisplayName { get; set; } = "SSO";
    public string Scopes { get; set; } = "openid email profile";
    // If set, the app asks for a work email and only these domains may use SSO
    public string[] Domains { get; set; } = [];

    public bool Enabled => !string.IsNullOrWhiteSpace(Authority) && !string.IsNullOrWhiteSpace(ClientId);
}
