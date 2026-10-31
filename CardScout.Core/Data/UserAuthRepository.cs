using System.Data;
using Dapper;
using Oracle.ManagedDataAccess.Client;
using CardScout.Core.Models;

namespace CardScout.Core.Data;

// Sign-in columns of CARD_USERS plus CARD_USER_IDENTITIES (external logins).
// Every method is keyed by a user id, a normalized email or a provider subject; nothing lists users.
// Times are UTC (see db/008_auth.sql).
public class UserAuthRepository
{
    private const string Now = "SYS_EXTRACT_UTC(SYSTIMESTAMP)";

    private const string UserColumns = @"
        ID AS Id, EMAIL AS Email, DISPLAY_NAME AS DisplayName, IS_ACTIVE AS IsActive,
        PASSWORD_HASH AS PasswordHash, EMAIL_VERIFIED AS EmailVerified,
        FAILED_LOGIN_COUNT AS FailedLoginCount, LOCKOUT_UNTIL AS LockoutUntil, CREATED_AT AS CreatedAt";

    private readonly string _connString;

    public UserAuthRepository(string connectionString)
    {
        _connString = connectionString;
    }

    public async Task<AuthUser?> GetByIdAsync(long userId)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<AuthUser>(
            $"SELECT {UserColumns} FROM CARD_USERS WHERE ID = :UserId", new { UserId = userId });
    }

    // email must already be normalized (trimmed, lowercased); matches older mixed-case rows too
    public async Task<AuthUser?> GetByEmailAsync(string email)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<AuthUser>(
            $"SELECT {UserColumns} FROM CARD_USERS WHERE LOWER(EMAIL) = :Email", new { Email = email });
    }

    // Compared in the database so the API and DB clocks can't disagree
    public async Task<bool> IsLockedOutAsync(long userId)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var locked = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM CARD_USERS WHERE ID = :UserId AND LOCKOUT_UNTIL > {Now}", new { UserId = userId });
        return locked > 0;
    }

    // Returns null when the email is taken (unique index), otherwise the new user id
    public async Task<long?> CreateUserAsync(string? email, string? displayName, string? passwordHash, bool emailVerified)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var sql = $@"
            INSERT INTO CARD_USERS (EMAIL, DISPLAY_NAME, PASSWORD_HASH, EMAIL_VERIFIED, PASSWORD_CHANGED_AT, CREATED_AT)
            VALUES (:Email, :DisplayName, :PasswordHash, :EmailVerified,
                    CASE WHEN :PasswordHash IS NULL THEN NULL ELSE {Now} END, SYSTIMESTAMP)
            RETURNING ID INTO :NewId";
        var p = new DynamicParameters(new
        {
            Email = email,
            DisplayName = displayName,
            PasswordHash = passwordHash,
            EmailVerified = emailVerified ? "Y" : "N",
        });
        p.Add("NewId", dbType: DbType.Int64, direction: ParameterDirection.Output);
        try
        {
            await conn.ExecuteAsync(sql, p);
        }
        catch (OracleException ex) when (ex.Number == 1) // ORA-00001 unique constraint
        {
            return null;
        }
        return p.Get<long>("NewId");
    }

    // Counts a failed password; at maxAttempts the account locks and the counter restarts
    public async Task RecordFailedLoginAsync(long userId, int maxAttempts, TimeSpan lockout)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var sql = $@"
            UPDATE CARD_USERS SET
                LOCKOUT_UNTIL = CASE WHEN NVL(FAILED_LOGIN_COUNT, 0) + 1 >= :MaxAttempts
                                     THEN {Now} + NUMTODSINTERVAL(:LockSeconds, 'SECOND') ELSE LOCKOUT_UNTIL END,
                FAILED_LOGIN_COUNT = CASE WHEN NVL(FAILED_LOGIN_COUNT, 0) + 1 >= :MaxAttempts
                                          THEN 0 ELSE NVL(FAILED_LOGIN_COUNT, 0) + 1 END
            WHERE ID = :UserId";
        await conn.ExecuteAsync(sql, new { UserId = userId, MaxAttempts = maxAttempts, LockSeconds = (int)lockout.TotalSeconds });
    }

    public async Task RecordLoginAsync(long userId)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            $"UPDATE CARD_USERS SET FAILED_LOGIN_COUNT = 0, LOCKOUT_UNTIL = NULL, LAST_LOGIN_AT = {Now} WHERE ID = :UserId",
            new { UserId = userId });
    }

    // Used by password reset (which also proves the user owns the email)
    public async Task SetPasswordAsync(long userId, string passwordHash, bool markEmailVerified)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var sql = $@"
            UPDATE CARD_USERS SET PASSWORD_HASH = :PasswordHash, PASSWORD_CHANGED_AT = {Now},
                FAILED_LOGIN_COUNT = 0, LOCKOUT_UNTIL = NULL,
                EMAIL_VERIFIED = CASE WHEN :MarkVerified = 'Y' THEN 'Y' ELSE EMAIL_VERIFIED END
            WHERE ID = :UserId";
        await conn.ExecuteAsync(sql, new { UserId = userId, PasswordHash = passwordHash, MarkVerified = markEmailVerified ? "Y" : "N" });
    }

    // PasswordHasher asked for a rehash (stronger iteration count); keeps everything else as is
    public async Task UpdatePasswordHashAsync(long userId, string passwordHash)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("UPDATE CARD_USERS SET PASSWORD_HASH = :PasswordHash WHERE ID = :UserId",
            new { UserId = userId, PasswordHash = passwordHash });
    }

    // A provider vouched for this email. If the account's email was never verified, a password on it
    // could have been set by someone else (account pre-hijacking), so that password is dropped.
    public async Task MarkEmailVerifiedAsync(long userId, bool dropUnverifiedPassword)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var sql = @"
            UPDATE CARD_USERS SET
                PASSWORD_HASH = CASE WHEN :DropPassword = 'Y' AND NVL(EMAIL_VERIFIED, 'N') <> 'Y' THEN NULL ELSE PASSWORD_HASH END,
                EMAIL_VERIFIED = 'Y'
            WHERE ID = :UserId";
        await conn.ExecuteAsync(sql, new { UserId = userId, DropPassword = dropUnverifiedPassword ? "Y" : "N" });
    }

    public async Task SetDisplayNameIfEmptyAsync(long userId, string displayName)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE CARD_USERS SET DISPLAY_NAME = :DisplayName WHERE ID = :UserId AND DISPLAY_NAME IS NULL",
            new { UserId = userId, DisplayName = displayName });
    }

    // ---- External identities ----

    public async Task<UserIdentity?> GetIdentityAsync(string provider, string subject)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = @"
            SELECT ID AS Id, USER_ID AS UserId, PROVIDER AS Provider, SUBJECT AS Subject, EMAIL AS Email,
                   EMAIL_VERIFIED AS EmailVerified, CREATED_AT AS CreatedAt
            FROM CARD_USER_IDENTITIES WHERE PROVIDER = :Provider AND SUBJECT = :Subject";
        return await conn.QuerySingleOrDefaultAsync<UserIdentity>(sql, new { Provider = provider, Subject = subject });
    }

    public async Task<List<string>> GetProvidersAsync(long userId)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<string>(
            "SELECT DISTINCT PROVIDER FROM CARD_USER_IDENTITIES WHERE USER_ID = :UserId", new { UserId = userId });
        return rows.ToList();
    }

    // Returns false if (provider, subject) is already linked (e.g. a concurrent first sign-in)
    public async Task<bool> AddIdentityAsync(long userId, string provider, string subject, string? email, bool emailVerified)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var sql = $@"
            INSERT INTO CARD_USER_IDENTITIES (USER_ID, PROVIDER, SUBJECT, EMAIL, EMAIL_VERIFIED, CREATED_AT, LAST_USED_AT)
            VALUES (:UserId, :Provider, :Subject, :Email, :EmailVerified, {Now}, {Now})";
        try
        {
            await conn.ExecuteAsync(sql, new
            {
                UserId = userId,
                Provider = provider,
                Subject = subject,
                Email = email,
                EmailVerified = emailVerified ? "Y" : "N",
            });
            return true;
        }
        catch (OracleException ex) when (ex.Number == 1)
        {
            return false;
        }
    }

    public async Task TouchIdentityAsync(long userId, long identityId, string? email, bool emailVerified)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            $@"UPDATE CARD_USER_IDENTITIES SET LAST_USED_AT = {Now}, EMAIL = NVL(:Email, EMAIL),
                   EMAIL_VERIFIED = CASE WHEN :Email IS NULL THEN EMAIL_VERIFIED ELSE :EmailVerified END
               WHERE ID = :Id AND USER_ID = :UserId",
            new { Id = identityId, UserId = userId, Email = email, EmailVerified = emailVerified ? "Y" : "N" });
    }
}
