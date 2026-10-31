using System.Globalization;
using System.Net.Mail;
using System.Text;

namespace CardScout.Api.Auth;

// NIST SP 800-63B style: length over composition rules, plus a blocklist of common passwords.
// Any characters (spaces, emoji, password-manager output) are allowed.
public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    // Common passwords that are long enough to pass the length check. Shorter ones fail on length anyway.
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "1234567890", "12345678910", "123456789012", "0123456789", "0987654321", "1111111111", "0000000000",
        "1122334455", "1234512345", "1234567890a", "1q2w3e4r5t", "1q2w3e4r5t6y", "1qaz2wsx3edc", "qwertyuiop",
        "qwertyuiop123", "qwerty1234", "qwerty12345", "qwerty123456", "asdfghjkl1", "asdfghjkl;", "zxcvbnm123",
        "password12", "password123", "password1234", "password12345", "password!!", "password123!", "passw0rd123",
        "p@ssword123", "p@ssw0rd123", "p@ssw0rd!!", "iloveyou12", "iloveyou123", "princess123", "football123",
        "baseball123", "basketball", "basketball1", "basketball123", "football12", "superman123", "batman1234",
        "starwars123", "sunshine123", "welcome123", "welcome1234", "letmein123", "letmein1234", "trustno1234",
        "monkey12345", "dragon12345", "michael123", "jennifer123", "jordan2323", "michaeljordan", "abcdefghij",
        "abcdefghijk", "abc1234567", "abcd123456", "a1b2c3d4e5", "aaaaaaaaaa", "changeme123", "administrator",
        "admin12345", "admin123456", "computer123", "internet123", "whatever123", "qazwsxedc123", "1qazxsw23edc",
        "cardscout", "cardscout1", "cardscout123", "cardscout1234", "sportscards", "sportscards1", "baseballcards",
        "pokemon123", "pokemon1234", "charizard123", "yankees123", "dodgers123", "cowboys123", "lakers1234",
    };

    // Null when acceptable, otherwise a message for the user
    public static string? Validate(string? password, string? email = null)
    {
        if (string.IsNullOrEmpty(password)) return "Enter a password.";
        var length = new StringInfo(password.Normalize(NormalizationForm.FormC)).LengthInTextElements;
        if (length < MinLength) return $"Use at least {MinLength} characters. A short phrase is easiest to remember.";
        if (password.Length > MaxLength) return $"Use at most {MaxLength} characters.";
        if (Common.Contains(password.Trim())) return "That password is too common. Pick something harder to guess.";
        if (password.Distinct().Count() < 4) return "That password is too easy to guess. Mix in more different characters.";

        var local = email?.Split('@')[0];
        if (local is { Length: >= 4 } && password.Contains(local, StringComparison.OrdinalIgnoreCase))
            return "Don't use your email address in your password.";
        return null;
    }

    // Trim + lowercase (the whole address: lookups are case-insensitive). Null if it isn't an email.
    public static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var trimmed = email.Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        if (trimmed.Length > 254 || trimmed.Contains(' ')) return null;
        if (!MailAddress.TryCreate(trimmed, out var parsed) || parsed.Address != trimmed) return null;
        var domain = parsed.Host;
        if (!domain.Contains('.') || domain.StartsWith('.') || domain.EndsWith('.')) return null;
        return trimmed;
    }
}
