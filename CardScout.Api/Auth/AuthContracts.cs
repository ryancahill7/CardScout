namespace CardScout.Api.Auth;

public record SignUpRequest(string? Email, string? Password, string? DisplayName);
public record SignInRequest(string? Email, string? Password);
public record RefreshRequest(string? RefreshToken);
public record GoogleSignInRequest(string? IdToken, string? Nonce);
// Apple only sends the name (and email) on the very first sign-in, so the app forwards them.
// Nonce is the raw value; the app passed its SHA-256 (hex) to Apple.
public record AppleSignInRequest(string? IdentityToken, string? Nonce, string? GivenName, string? FamilyName);
public record CompleteSignInRequest(string? Code, string? CodeVerifier);
public record ForgotPasswordRequest(string? Email);
public record ResetPasswordRequest(string? Token, string? NewPassword);

public record AuthUserDto(long Id, string? Email, string? DisplayName, bool EmailVerified, bool HasPassword, List<string> Providers);

public record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    AuthUserDto User);

public record MeResponse(AuthUserDto User, string AuthMethod);

public record SsoProvider(bool Enabled, string DisplayName, bool RequiresEmail);
public record AuthProvidersResponse(bool Password, bool Apple, bool Google, bool Ebay, SsoProvider Sso);
