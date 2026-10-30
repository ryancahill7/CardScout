namespace CardScout.Api;

// The authenticated user for the current request. Set by ApiKeyMiddleware; endpoints read
// UserId from here and pass it to every repository call.
public class CurrentUser
{
    private long? _userId;

    public bool IsAuthenticated => _userId.HasValue;

    public long UserId => _userId ?? throw new InvalidOperationException("No authenticated user for this request");

    // Image keys for a user live under this prefix, so ownership can be checked from the path
    public string ImageFolder => $"users/{UserId}";

    public void Set(long userId) => _userId = userId;
}
