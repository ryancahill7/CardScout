namespace CardScout.Core.Models;

// A CARD_USERS row with the sign-in columns (CardUser is the plain profile)
public class AuthUser
{
    public long Id { get; set; }
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public string IsActive { get; set; } = "Y";
    public string? PasswordHash { get; set; }
    public string? EmailVerified { get; set; }
    public int? FailedLoginCount { get; set; }
    public DateTime? LockoutUntil { get; set; }
    public DateTime CreatedAt { get; set; }

    public bool Active => IsActive == "Y";
    public bool IsEmailVerified => EmailVerified == "Y";
    public bool HasPassword => !string.IsNullOrEmpty(PasswordHash);
}

public class UserIdentity
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Provider { get; set; } = "";
    public string Subject { get; set; } = "";
    public string? Email { get; set; }
    public string EmailVerified { get; set; } = "N";
    public DateTime CreatedAt { get; set; }
}

public class RefreshTokenRecord
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string FamilyId { get; set; } = "";
    public string? AuthMethod { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class AuthFlow
{
    public long Id { get; set; }
    public string Provider { get; set; } = "";
    public string AppRedirectUri { get; set; } = "";
    public string AppCodeChallenge { get; set; } = "";
    public string? ProviderVerifier { get; set; }
    public string? Nonce { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? CallbackAt { get; set; }
    public long? UserId { get; set; }
    public DateTime? CodeExpiresAt { get; set; }
    public DateTime? CodeUsedAt { get; set; }
}
