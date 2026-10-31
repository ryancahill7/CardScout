namespace CardScout.Api;

// The authenticated user for the current request. Set by ApiKeyMiddleware (bearer token or API key);
// endpoints read UserId from here and pass it to every repository call.
public class CurrentUser
{
    private long? _userId;

    public bool IsAuthenticated => _userId.HasValue;

    public long UserId => _userId ?? throw new InvalidOperationException("No authenticated user for this request");

    // How this request authenticated: "apikey", or the sign-in method behind the bearer token
    // ("password", "apple", "google", "ebay", "oidc")
    public string? AuthMethod { get; private set; }

    // Image keys for a user live under this prefix, so ownership can be checked from the path
    public string ImageFolder => $"users/{UserId}";

    public void Set(long userId, string? authMethod = null)
    {
        _userId = userId;
        AuthMethod = authMethod;
    }
}
