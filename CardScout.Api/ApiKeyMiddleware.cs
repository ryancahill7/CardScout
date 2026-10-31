using CardScout.Core.Data;
using Microsoft.Extensions.Caching.Memory;

namespace CardScout.Api;

// Per-user API keys: each request's X-Api-Key is hashed and looked up in CARD_USERS.
// To move to a real identity provider later, replace this with JWT auth that maps the
// token's issuer + subject to CARD_USERS.AUTH_PROVIDER/AUTH_SUBJECT and calls CurrentUser.Set.
public class ApiKeyMiddleware
{
    public const string HeaderName = "X-Api-Key";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly RequestDelegate _next;

    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, CurrentUser currentUser, CardUsersRepository users, IMemoryCache cache)
    {
        // The Stripe webhook has no API key; it's authenticated by its Stripe-Signature header instead
        if (!context.Request.Path.StartsWithSegments("/api") || HttpMethods.IsOptions(context.Request.Method)
            || context.Request.Path.Equals(Endpoints.BillingEndpoints.WebhookPath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var apiKey = context.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            await RejectAsync(context);
            return;
        }

        // Cache by hash, never the raw key; deactivated users stop working within CacheDuration
        var cacheKey = "user:" + CardUsersRepository.HashApiKey(apiKey);
        if (!cache.TryGetValue(cacheKey, out long? userId))
        {
            var user = await users.GetActiveByApiKeyAsync(apiKey);
            userId = user?.Id;
            cache.Set(cacheKey, userId, CacheDuration);
        }

        if (userId == null)
        {
            await RejectAsync(context);
            return;
        }

        currentUser.Set(userId.Value);
        await _next(context);
    }

    private static async Task RejectAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid API key." });
    }
}
