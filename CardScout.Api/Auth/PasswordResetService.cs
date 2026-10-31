using CardScout.Core.Data;
using Microsoft.Extensions.Options;

namespace CardScout.Api.Auth;

// Forgot password → emailed single-use link (hashed in the DB, short expiry) → new password.
public class PasswordResetService
{
    private const int MaxLinksPerHour = 3;

    private readonly UserAuthRepository _users;
    private readonly PasswordResetRepository _resets;
    private readonly RefreshTokenRepository _refreshTokens;
    private readonly AuthService _auth;
    private readonly IEmailSender _email;
    private readonly AuthOptions _options;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(UserAuthRepository users, PasswordResetRepository resets, RefreshTokenRepository refreshTokens,
        AuthService auth, IEmailSender email, IOptions<AuthOptions> options, ILogger<PasswordResetService> logger)
    {
        _users = users;
        _resets = resets;
        _refreshTokens = refreshTokens;
        _auth = auth;
        _email = email;
        _options = options.Value;
        _logger = logger;
    }

    // Always "succeeds" from the caller's point of view, so it can't be used to discover accounts
    public async Task RequestAsync(string? rawEmail)
    {
        var email = PasswordPolicy.NormalizeEmail(rawEmail);
        if (email == null) return;
        var user = await _users.GetByEmailAsync(email);
        if (user is not { Active: true } || string.IsNullOrEmpty(user.Email)) return;
        if (await _resets.CountRecentAsync(user.Id, TimeSpan.FromHours(1)) >= MaxLinksPerHour)
        {
            _logger.LogInformation("Password reset for user {UserId} skipped: hourly limit reached", user.Id);
            return;
        }

        var token = TokenService.NewOpaqueToken();
        var lifetime = TimeSpan.FromMinutes(_options.PasswordResetMinutes);
        await _resets.CreateAsync(user.Id, TokenService.Hash(token), lifetime);

        var separator = _options.PasswordResetUrl.Contains('?') ? '&' : '?';
        var link = $"{_options.PasswordResetUrl}{separator}token={Uri.EscapeDataString(token)}";
        var body =
            $"Someone (hopefully you) asked to reset the password for your CardScout account.\n\n" +
            $"Open this link on your phone to choose a new password:\n{link}\n\n" +
            $"Or paste this reset code into the app: {token}\n\n" +
            $"The link works once and expires in {_options.PasswordResetMinutes} minutes. " +
            "If you didn't ask for this, ignore this email; your password hasn't changed.";
        try
        {
            await _email.SendAsync(user.Email, "Reset your CardScout password", body);
            _logger.LogInformation("Password reset link issued for user {UserId}", user.Id);
        }
        catch (Exception ex)
        {
            // Same response as for an unknown email, so a send failure doesn't reveal the account exists
            _logger.LogError(ex, "Couldn't send the password reset email for user {UserId}", user.Id);
        }
    }

    // Null on success, otherwise an error for the user
    public async Task<string?> ResetAsync(string? token, string? newPassword)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200) return "This reset link is invalid or has expired.";

        // Check the password before spending the token, so a weak choice doesn't burn the link.
        // (The email isn't known yet; the policy's email check is skipped here.)
        var passwordError = PasswordPolicy.Validate(newPassword);
        if (passwordError != null) return passwordError;

        var userId = await _resets.ConsumeAsync(TokenService.Hash(token.Trim()));
        if (userId == null) return "This reset link is invalid or has expired. Request a new one.";

        var user = await _users.GetByIdAsync(userId.Value);
        if (user is not { Active: true }) return "This reset link is invalid or has expired. Request a new one.";

        // Using the emailed link proves the user owns the address
        await _users.SetPasswordAsync(user.Id, _auth.HashPassword(newPassword!), markEmailVerified: true);
        var revoked = await _refreshTokens.RevokeAllForUserAsync(user.Id, "password-reset");
        _logger.LogInformation("User {UserId} reset their password; signed out {Count} sessions", user.Id, revoked);
        return null;
    }
}
