using System.Net;
using CardScout.Api.Auth;
using CardScout.Core.Data;
using CardScout.Core.Images;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CardScout.Api.Endpoints;

// Sign-up / sign-in / sessions / federated sign-in / password reset / account deletion.
// Everything here except /me and DELETE /account is anonymous (AllowAnonymous metadata, which
// ApiKeyMiddleware honours) and rate limited per client IP.
public static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";
    public const string StrictRateLimitPolicy = "auth-strict";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var open = app.MapGroup("/api/auth").AllowAnonymous().RequireRateLimiting(RateLimitPolicy);
        open.MapGet("/providers", Providers);
        open.MapPost("/signin", SignInAsync);
        open.MapPost("/refresh", RefreshAsync);
        open.MapPost("/logout", LogoutAsync);
        open.MapPost("/google", GoogleAsync);
        open.MapPost("/apple", AppleAsync);
        open.MapGet("/{provider}/start", StartAsync);
        open.MapGet("/{provider}/callback", CallbackAsync);
        open.MapPost("/{provider}/complete", CompleteAsync);

        var strict = app.MapGroup("/api/auth").AllowAnonymous().RequireRateLimiting(StrictRateLimitPolicy);
        strict.MapPost("/signup", SignUpAsync);
        strict.MapPost("/password/forgot", ForgotPasswordAsync);
        strict.MapPost("/password/reset", ResetPasswordAsync);

        // Signed-in (bearer token or API key)
        app.MapGet("/api/auth/me", MeAsync);
        app.MapDelete("/api/auth/account", DeleteAccountAsync);
    }

    private static IResult Error(int status, string message) => Results.Json(new { error = message }, statusCode: status);

    private static IResult FromResult(AuthResult result) => result.Outcome switch
    {
        AuthOutcome.Ok => Results.Ok(result.Session),
        AuthOutcome.InvalidCredentials => Error(StatusCodes.Status401Unauthorized, result.Error!),
        AuthOutcome.LockedOut => Error(StatusCodes.Status429TooManyRequests, result.Error!),
        AuthOutcome.EmailTaken => Error(StatusCodes.Status409Conflict, result.Error!),
        AuthOutcome.Disabled => Error(StatusCodes.Status403Forbidden, result.Error!),
        _ => Error(StatusCodes.Status400BadRequest, result.Error ?? "Invalid request."),
    };

    private static IResult Providers(IOptions<AuthOptions> options, BrokeredSignInService brokered)
    {
        var o = options.Value;
        return Results.Ok(new AuthProvidersResponse(
            Password: true,
            Apple: o.Apple.Enabled,
            Google: brokered.IsEnabled("google"),
            Ebay: brokered.IsEnabled("ebay"),
            Sso: new SsoProvider(brokered.IsEnabled("oidc"), o.Oidc.DisplayName, o.Oidc.Domains.Length > 0)));
    }

    private static async Task<IResult> SignUpAsync(SignUpRequest request, AuthService auth) =>
        FromResult(await auth.SignUpAsync(request));

    private static async Task<IResult> SignInAsync(SignInRequest request, AuthService auth) =>
        FromResult(await auth.SignInAsync(request));

    private static async Task<IResult> RefreshAsync(RefreshRequest request, AuthService auth)
    {
        var session = await auth.RefreshAsync(request.RefreshToken);
        return session == null ? Error(StatusCodes.Status401Unauthorized, "Session expired. Sign in again.") : Results.Ok(session);
    }

    private static async Task<IResult> LogoutAsync(RefreshRequest request, AuthService auth)
    {
        await auth.LogoutAsync(request.RefreshToken);
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(CurrentUser user, AuthService auth)
    {
        var dto = await auth.GetUserDtoAsync(user.UserId);
        return dto == null ? Error(StatusCodes.Status401Unauthorized, "Account not found.") : Results.Ok(new MeResponse(dto, user.AuthMethod ?? "unknown"));
    }

    // For native Google Sign-In (a development build with Google's SDK): the app posts Google's ID token
    private static async Task<IResult> GoogleAsync(GoogleSignInRequest request, ExternalTokenValidator validator,
        IOptions<AuthOptions> options, AuthService auth, ILogger<AuthService> logger)
    {
        var audiences = options.Value.Google.Audiences.ToList();
        if (audiences.Count == 0) return Error(StatusCodes.Status404NotFound, "Google sign-in isn't available.");
        if (string.IsNullOrWhiteSpace(request.IdToken)) return Error(StatusCodes.Status400BadRequest, "idToken is required.");

        try
        {
            var jwt = await validator.ValidateAsync(request.IdToken, ExternalTokenValidator.GoogleAuthority, audiences,
                ["accounts.google.com"], expectedNonce: string.IsNullOrEmpty(request.Nonce) ? null : request.Nonce);
            var identity = new ExternalIdentity("google", jwt.Subject,
                ExternalTokenValidator.StringClaim(jwt, "email"), ExternalTokenValidator.ClaimIsTrue(jwt, "email_verified"),
                ExternalTokenValidator.StringClaim(jwt, "name"));
            return FromResult(await auth.SignInExternalAsync(identity));
        }
        catch (SecurityTokenException ex)
        {
            logger.LogInformation("Rejected Google ID token: {Reason}", ex.Message);
            return Error(StatusCodes.Status401Unauthorized, "Google sign-in failed. Try again.");
        }
    }

    // Sign in with Apple (expo-apple-authentication works in Expo Go on iOS)
    private static async Task<IResult> AppleAsync(AppleSignInRequest request, ExternalTokenValidator validator,
        IOptions<AuthOptions> options, AuthService auth, ILogger<AuthService> logger)
    {
        var apple = options.Value.Apple;
        if (!apple.Enabled) return Error(StatusCodes.Status404NotFound, "Sign in with Apple isn't available.");
        if (string.IsNullOrWhiteSpace(request.IdentityToken) || string.IsNullOrWhiteSpace(request.Nonce))
            return Error(StatusCodes.Status400BadRequest, "identityToken and nonce are required.");

        try
        {
            // The app gave Apple SHA-256(rawNonce); the token carries that, so a replayed token fails here
            var jwt = await validator.ValidateAsync(request.IdentityToken, ExternalTokenValidator.AppleAuthority, apple.Audiences,
                expectedNonce: ExternalTokenValidator.Sha256Hex(request.Nonce));
            var name = string.Join(' ', new[] { request.GivenName, request.FamilyName }.Where(s => !string.IsNullOrWhiteSpace(s))!.Select(s => s!.Trim()));
            var identity = new ExternalIdentity("apple", jwt.Subject,
                ExternalTokenValidator.StringClaim(jwt, "email"), ExternalTokenValidator.ClaimIsTrue(jwt, "email_verified"),
                string.IsNullOrWhiteSpace(name) ? null : name[..Math.Min(name.Length, 100)]);
            return FromResult(await auth.SignInExternalAsync(identity));
        }
        catch (SecurityTokenException ex)
        {
            logger.LogInformation("Rejected Apple identity token: {Reason}", ex.Message);
            return Error(StatusCodes.Status401Unauthorized, "Sign in with Apple failed. Try again.");
        }
    }

    // GET /api/auth/{google|ebay|oidc}/start?redirectUri=...&codeChallenge=...[&email=...]
    private static async Task<IResult> StartAsync(string provider, string? redirectUri, string? codeChallenge, string? email,
        HttpRequest request, BrokeredSignInService brokered)
    {
        if (!BrokeredSignInService.Providers.Contains(provider)) return Error(StatusCodes.Status404NotFound, "Unknown provider.");
        var (url, error) = await brokered.StartAsync(provider, redirectUri, codeChallenge, email, request);
        if (url != null) return Results.Redirect(url);
        // This runs in the in-app browser: send the message back to the app when we safely can
        if (brokered.IsAllowedAppRedirect(redirectUri))
            return Results.Redirect(redirectUri + (redirectUri!.Contains('?') ? "&" : "?") + "error=" + Uri.EscapeDataString(error!));
        return Error(StatusCodes.Status400BadRequest, error!);
    }

    private static async Task<IResult> CallbackAsync(string provider, HttpRequest request, BrokeredSignInService brokered)
    {
        if (!BrokeredSignInService.Providers.Contains(provider)) return Error(StatusCodes.Status404NotFound, "Unknown provider.");
        var next = await brokered.CallbackAsync(provider, request.Query, request);
        if (next != null) return Results.Redirect(next);
        return Results.Content(
            "<!DOCTYPE html><html><head><meta name=viewport content=\"width=device-width\"><title>CardScout</title></head>" +
            "<body style=\"font-family:-apple-system,sans-serif;background:#06080D;color:#F3F5FA;padding:32px\">" +
            "<h2>Sign-in link expired</h2><p>Close this window, go back to CardScout and try again.</p></body></html>",
            "text/html", statusCode: (int)HttpStatusCode.BadRequest);
    }

    private static async Task<IResult> CompleteAsync(string provider, CompleteSignInRequest request, BrokeredSignInService brokered)
    {
        if (!BrokeredSignInService.Providers.Contains(provider)) return Error(StatusCodes.Status404NotFound, "Unknown provider.");
        var session = await brokered.CompleteAsync(request);
        return session == null ? Error(StatusCodes.Status401Unauthorized, "Sign-in expired. Try again.") : Results.Ok(session);
    }

    private static async Task<IResult> ForgotPasswordAsync(ForgotPasswordRequest request, PasswordResetService resets)
    {
        await resets.RequestAsync(request.Email);
        return Results.Accepted(value: new { message = "If an account uses that email, a reset link is on its way." });
    }

    private static async Task<IResult> ResetPasswordAsync(ResetPasswordRequest request, PasswordResetService resets)
    {
        var error = await resets.ResetAsync(request.Token, request.NewPassword);
        return error == null ? Results.NoContent() : Error(StatusCodes.Status400BadRequest, error);
    }

    // Permanently deletes the signed-in user and everything they own (App Store requirement)
    private static async Task<IResult> DeleteAccountAsync(HttpContext context, CurrentUser user, AccountDeletionRepository deletion,
        IImageStorage images, IMemoryCache cache, ILogger<AuthService> logger)
    {
        var userId = user.UserId;
        var counts = await deletion.DeleteUserAsync(userId);
        try
        {
            await images.DeleteFolderAsync(user.ImageFolder);
        }
        catch (Exception ex)
        {
            // The account is already gone; leftover files are orphaned, not reachable
            logger.LogError(ex, "Deleted user {UserId} but couldn't remove their image folder", userId);
        }

        ApiKeyMiddleware.Forget(cache, userId, context.Request.Headers[ApiKeyMiddleware.HeaderName].ToString());
        logger.LogWarning("Deleted account {UserId}: {Counts}", userId,
            string.Join(", ", counts.Select(kv => $"{kv.Key}={kv.Value}")));
        return Results.NoContent();
    }
}
