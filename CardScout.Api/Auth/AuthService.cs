using CardScout.Core.Data;
using CardScout.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CardScout.Api.Auth;

public enum AuthOutcome { Ok, InvalidCredentials, LockedOut, EmailTaken, Invalid, Disabled }

public record AuthResult(AuthOutcome Outcome, AuthResponse? Session = null, string? Error = null)
{
    public static AuthResult Fail(AuthOutcome outcome, string error) => new(outcome, null, error);
}

// Email/password accounts, sessions (access + rotating refresh tokens) and linking external identities.
public class AuthService
{
    public const string InvalidCredentialsMessage = "Email or password is incorrect.";
    public const string LockedOutMessage = "Too many sign-in attempts. Wait a few minutes or reset your password.";

    private readonly UserAuthRepository _users;
    private readonly RefreshTokenRepository _refreshTokens;
    private readonly TokenService _tokens;
    private readonly IMemoryCache _cache;
    private readonly AuthOptions _options;
    private readonly ILogger<AuthService> _logger;
    private readonly PasswordHasher<AuthUser> _hasher = new(); // PBKDF2-HMAC-SHA512, 100k iterations (v3 format)

    // Verified against when the email has no password, so a miss takes as long as a wrong password
    private readonly string _dummyHash;

    public AuthService(UserAuthRepository users, RefreshTokenRepository refreshTokens, TokenService tokens,
        IMemoryCache cache, IOptions<AuthOptions> options, ILogger<AuthService> logger)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _tokens = tokens;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
        _dummyHash = _hasher.HashPassword(new AuthUser(), TokenService.NewOpaqueToken());
    }

    public string HashPassword(string password) => _hasher.HashPassword(new AuthUser(), password);

    public async Task<AuthResult> SignUpAsync(SignUpRequest request)
    {
        var email = PasswordPolicy.NormalizeEmail(request.Email);
        if (email == null) return AuthResult.Fail(AuthOutcome.Invalid, "Enter a valid email address.");
        var passwordError = PasswordPolicy.Validate(request.Password, email);
        if (passwordError != null) return AuthResult.Fail(AuthOutcome.Invalid, passwordError);

        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
        if (displayName is { Length: > 100 }) displayName = displayName[..100];

        var userId = await _users.CreateUserAsync(email, displayName, HashPassword(request.Password!), emailVerified: false);
        if (userId == null)
            return AuthResult.Fail(AuthOutcome.EmailTaken,
                "Couldn't create an account with that email. If it's yours, sign in or reset your password.");

        _logger.LogInformation("User {UserId} signed up with a password", userId);
        await _users.RecordLoginAsync(userId.Value);
        return new AuthResult(AuthOutcome.Ok, await IssueSessionAsync(userId.Value, "password"));
    }

    public async Task<AuthResult> SignInAsync(SignInRequest request)
    {
        var email = PasswordPolicy.NormalizeEmail(request.Email);
        if (email == null || string.IsNullOrEmpty(request.Password) || request.Password.Length > PasswordPolicy.MaxLength)
            return AuthResult.Fail(AuthOutcome.InvalidCredentials, InvalidCredentialsMessage);

        var user = await _users.GetByEmailAsync(email);

        // Unknown emails get the same lockout behaviour as real ones (tracked in memory), so the
        // response never reveals whether an account exists
        if (user == null || !user.Active || !user.HasPassword)
        {
            var missKey = "auth-miss:" + email;
            var misses = _cache.TryGetValue(missKey, out int m) ? m : 0;
            if (misses >= _options.MaxFailedLogins) return AuthResult.Fail(AuthOutcome.LockedOut, LockedOutMessage);
            _hasher.VerifyHashedPassword(new AuthUser(), _dummyHash, request.Password);
            _cache.Set(missKey, misses + 1, TimeSpan.FromMinutes(_options.LockoutMinutes));
            return AuthResult.Fail(AuthOutcome.InvalidCredentials, InvalidCredentialsMessage);
        }

        if (await _users.IsLockedOutAsync(user.Id))
            return AuthResult.Fail(AuthOutcome.LockedOut, LockedOutMessage);

        var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash!, request.Password);
        if (verify == PasswordVerificationResult.Failed)
        {
            await _users.RecordFailedLoginAsync(user.Id, _options.MaxFailedLogins, TimeSpan.FromMinutes(_options.LockoutMinutes));
            _logger.LogInformation("Failed password sign-in for user {UserId}", user.Id);
            return AuthResult.Fail(AuthOutcome.InvalidCredentials, InvalidCredentialsMessage);
        }
        if (verify == PasswordVerificationResult.SuccessRehashNeeded)
            await _users.UpdatePasswordHashAsync(user.Id, HashPassword(request.Password));

        await _users.RecordLoginAsync(user.Id);
        return new AuthResult(AuthOutcome.Ok, await IssueSessionAsync(user.Id, "password"));
    }

    // Finds the user for an external login, linking or creating as needed, and starts a session.
    // Linking to an existing account by email only happens when the provider says the email is verified.
    public async Task<AuthResult> SignInExternalAsync(ExternalIdentity ext)
    {
        var userId = await ResolveExternalUserAsync(ext);
        if (userId == null) return AuthResult.Fail(AuthOutcome.Disabled, "This account can't sign in.");
        await _users.RecordLoginAsync(userId.Value);
        return new AuthResult(AuthOutcome.Ok, await IssueSessionAsync(userId.Value, ext.Provider));
    }

    public async Task<long?> ResolveExternalUserAsync(ExternalIdentity ext)
    {
        var email = PasswordPolicy.NormalizeEmail(ext.Email);
        var verified = ext.EmailVerified && email != null;

        var identity = await _users.GetIdentityAsync(ext.Provider, ext.Subject);
        if (identity != null)
        {
            var linked = await _users.GetByIdAsync(identity.UserId);
            if (linked is not { Active: true }) return null;
            await _users.TouchIdentityAsync(linked.Id, identity.Id, email, verified);
            if (ext.DisplayName != null) await _users.SetDisplayNameIfEmptyAsync(linked.Id, ext.DisplayName);
            return linked.Id;
        }

        if (verified)
        {
            var existing = await _users.GetByEmailAsync(email!);
            if (existing != null)
            {
                if (!existing.Active) return null;
                await LinkVerifiedEmailAsync(existing, ext, email!);
                return existing.Id;
            }
        }

        // New user. An unverified email is kept on the identity only, never as the account email.
        var newId = await _users.CreateUserAsync(verified ? email : null, ext.DisplayName, null, verified);
        if (newId == null)
        {
            // Lost a race with another sign-in for the same verified email: link to that one instead
            var existing = verified ? await _users.GetByEmailAsync(email!) : null;
            if (existing is not { Active: true }) return null;
            await LinkVerifiedEmailAsync(existing, ext, email!);
            return existing.Id;
        }

        if (!await _users.AddIdentityAsync(newId.Value, ext.Provider, ext.Subject, email, verified))
        {
            // Concurrent first sign-in linked the identity first; use that user (the new row stays unused)
            var winner = await _users.GetIdentityAsync(ext.Provider, ext.Subject);
            _logger.LogWarning("Concurrent first sign-in for {Provider}; user {Orphan} is unused", ext.Provider, newId);
            return winner?.UserId;
        }
        _logger.LogInformation("User {UserId} signed up with {Provider}", newId, ext.Provider);
        return newId;
    }

    private async Task LinkVerifiedEmailAsync(AuthUser existing, ExternalIdentity ext, string email)
    {
        await _users.AddIdentityAsync(existing.Id, ext.Provider, ext.Subject, email, true);

        // Pre-hijacking defence: a password on an account whose email was never verified may have been
        // set by someone else before the real owner showed up. Drop it and end its sessions.
        var dropPassword = existing.HasPassword && !existing.IsEmailVerified;
        await _users.MarkEmailVerifiedAsync(existing.Id, dropUnverifiedPassword: true);
        if (dropPassword)
        {
            await _refreshTokens.RevokeAllForUserAsync(existing.Id, "email-verified-link");
            _logger.LogWarning("User {UserId}: unverified password removed when linking verified {Provider} identity",
                existing.Id, ext.Provider);
        }
        if (ext.DisplayName != null) await _users.SetDisplayNameIfEmptyAsync(existing.Id, ext.DisplayName);
        _logger.LogInformation("Linked {Provider} identity to user {UserId} by verified email", ext.Provider, existing.Id);
    }

    public async Task<AuthResponse> IssueSessionAsync(long userId, string authMethod, string? familyId = null)
    {
        familyId ??= Guid.NewGuid().ToString("N");
        var refresh = TokenService.NewOpaqueToken();
        await _refreshTokens.InsertAsync(userId, TokenService.Hash(refresh), familyId, authMethod, _tokens.RefreshTokenLifetime);
        var (access, accessExpires) = _tokens.CreateAccessToken(userId, familyId, authMethod);
        return new AuthResponse(access, accessExpires, refresh, DateTime.UtcNow.Add(_tokens.RefreshTokenLifetime),
            await GetUserDtoAsync(userId) ?? throw new InvalidOperationException($"User {userId} vanished"));
    }

    // Rotation: the presented token is spent and a new one is issued in the same family.
    // A spent token coming back means it was copied, so the whole family is revoked.
    public async Task<AuthResponse?> RefreshAsync(string? refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 200) return null;
        var hash = TokenService.Hash(refreshToken);
        var (token, reused, existing) = await _refreshTokens.ConsumeAsync(hash);

        if (token == null)
        {
            if (reused && existing != null)
            {
                var revoked = await _refreshTokens.RevokeFamilyAsync(existing.UserId, existing.FamilyId, "reuse-detected");
                _logger.LogWarning("Refresh token reuse for user {UserId}; revoked {Count} tokens in the session",
                    existing.UserId, revoked);
            }
            return null;
        }

        var user = await _users.GetByIdAsync(token.UserId);
        if (user is not { Active: true }) return null;
        return await IssueSessionAsync(token.UserId, token.AuthMethod ?? "password", token.FamilyId);
    }

    public async Task LogoutAsync(string? refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 200) return;
        var token = await _refreshTokens.GetByHashAsync(TokenService.Hash(refreshToken));
        if (token != null) await _refreshTokens.RevokeFamilyAsync(token.UserId, token.FamilyId, "logout");
    }

    public async Task<AuthUserDto?> GetUserDtoAsync(long userId)
    {
        var user = await _users.GetByIdAsync(userId);
        if (user == null) return null;
        var providers = await _users.GetProvidersAsync(userId);
        return new AuthUserDto(user.Id, user.Email, user.DisplayName, user.IsEmailVerified, user.HasPassword, providers);
    }
}
