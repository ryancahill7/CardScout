using CardScout.Api.Auth;
using CardScout.Core.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;

namespace CardScout.Api;

// Authenticates every /api request and sets the scoped CurrentUser, which is all endpoints use.
// Two credentials are accepted:
//   Authorization: Bearer <access JWT>  — the app after sign-in (see Endpoints/AuthEndpoints.cs)
//   X-Api-Key: <personal key>           — scripts, dev setups and existing users (SHA-256 lookup in CARD_USERS)
// Endpoints marked AllowAnonymous (sign-in, refresh, webhooks...) skip this.
public class ApiKeyMiddleware
{
    public const string HeaderName = "X-Api-Key";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    // Bearer users are re-checked more often so a deleted or deactivated account stops quickly
    private static readonly TimeSpan ActiveCheckDuration = TimeSpan.FromMinutes(1);

    private readonly RequestDelegate _next;

    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, CurrentUser currentUser, CardUsersRepository users,
        UserAuthRepository authUsers, TokenService tokens, IMemoryCache cache)
    {
        if (!context.Request.Path.StartsWithSegments("/api")
            || HttpMethods.IsOptions(context.Request.Method)
            || context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() != null)
        {
            await _next(context);
            return;
        }

        var authorization = context.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var validated = await tokens.ValidateAccessTokenAsync(authorization["Bearer ".Length..].Trim());
            if (validated == null || !await IsActiveAsync(validated.Value.UserId, authUsers, cache))
            {
                // The app refreshes its session when it sees this
                context.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
                await RejectAsync(context, "Session expired. Sign in again.");
                return;
            }
            currentUser.Set(validated.Value.UserId, validated.Value.AuthMethod ?? "bearer");
            await _next(context);
            return;
        }

        var apiKey = context.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            await RejectAsync(context, "Sign in required.");
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
            await RejectAsync(context, "Missing or invalid API key.");
            return;
        }

        currentUser.Set(userId.Value, "apikey");
        await _next(context);
    }

    private static async Task<bool> IsActiveAsync(long userId, UserAuthRepository authUsers, IMemoryCache cache)
    {
        var key = "active:" + userId;
        if (cache.TryGetValue(key, out bool active)) return active;
        var user = await authUsers.GetByIdAsync(userId);
        active = user is { Active: true };
        cache.Set(key, active, ActiveCheckDuration);
        return active;
    }

    // After account deletion: drop cached lookups so the credentials stop working immediately
    public static void Forget(IMemoryCache cache, long userId, string? apiKey)
    {
        cache.Remove("active:" + userId);
        if (!string.IsNullOrWhiteSpace(apiKey)) cache.Remove("user:" + CardUsersRepository.HashApiKey(apiKey));
    }

    private static async Task RejectAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = message });
    }
}
