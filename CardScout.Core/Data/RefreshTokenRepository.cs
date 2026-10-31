using Dapper;
using Oracle.ManagedDataAccess.Client;
using CardScout.Core.Models;

namespace CardScout.Core.Data;

// CARD_REFRESH_TOKENS. Only SHA-256 hashes of tokens are stored. Times are UTC.
public class RefreshTokenRepository
{
    private const string Now = "SYS_EXTRACT_UTC(SYSTIMESTAMP)";

    private readonly string _connString;

    public RefreshTokenRepository(string connectionString)
    {
        _connString = connectionString;
    }

    public async Task InsertAsync(long userId, string tokenHash, string familyId, string authMethod, TimeSpan lifetime)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var sql = $@"
            INSERT INTO CARD_REFRESH_TOKENS (USER_ID, TOKEN_HASH, FAMILY_ID, AUTH_METHOD, CREATED_AT, EXPIRES_AT)
            VALUES (:UserId, :TokenHash, :FamilyId, :AuthMethod, {Now}, {Now} + NUMTODSINTERVAL(:Seconds, 'SECOND'))";
        await conn.ExecuteAsync(sql, new
        {
            UserId = userId,
            TokenHash = tokenHash,
            FamilyId = familyId,
            AuthMethod = authMethod,
            Seconds = (long)lifetime.TotalSeconds,
        });
    }

    // Marks the token used in one statement, so two concurrent refreshes can't both succeed.
    // Returns the token row when it was live; otherwise null plus whether it had already been used.
    public async Task<(RefreshTokenRecord? Token, bool Reused, RefreshTokenRecord? Existing)> ConsumeAsync(string tokenHash)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();

        var consumed = await conn.ExecuteAsync($@"
            UPDATE CARD_REFRESH_TOKENS SET USED_AT = {Now}
            WHERE TOKEN_HASH = :TokenHash AND USED_AT IS NULL AND REVOKED_AT IS NULL AND EXPIRES_AT > {Now}",
            new { TokenHash = tokenHash });

        var row = await conn.QuerySingleOrDefaultAsync<RefreshTokenRecord>(@"
            SELECT ID AS Id, USER_ID AS UserId, FAMILY_ID AS FamilyId, AUTH_METHOD AS AuthMethod,
                   EXPIRES_AT AS ExpiresAt, USED_AT AS UsedAt, REVOKED_AT AS RevokedAt
            FROM CARD_REFRESH_TOKENS WHERE TOKEN_HASH = :TokenHash", new { TokenHash = tokenHash });

        if (consumed == 1) return (row, false, row);
        // Not consumed now but already used before: a rotated-out token came back
        return (null, row?.UsedAt != null, row);
    }

    public async Task<RefreshTokenRecord?> GetByHashAsync(string tokenHash)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<RefreshTokenRecord>(@"
            SELECT ID AS Id, USER_ID AS UserId, FAMILY_ID AS FamilyId, AUTH_METHOD AS AuthMethod,
                   EXPIRES_AT AS ExpiresAt, USED_AT AS UsedAt, REVOKED_AT AS RevokedAt
            FROM CARD_REFRESH_TOKENS WHERE TOKEN_HASH = :TokenHash", new { TokenHash = tokenHash });
    }

    // Ends one sign-in session (every token in its rotation chain)
    public async Task<int> RevokeFamilyAsync(long userId, string familyId, string reason)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        return await conn.ExecuteAsync($@"
            UPDATE CARD_REFRESH_TOKENS SET REVOKED_AT = {Now}, REVOKE_REASON = :Reason
            WHERE USER_ID = :UserId AND FAMILY_ID = :FamilyId AND REVOKED_AT IS NULL",
            new { UserId = userId, FamilyId = familyId, Reason = reason });
    }

    // Signs the user out everywhere (password reset, account takeover protection)
    public async Task<int> RevokeAllForUserAsync(long userId, string reason)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        return await conn.ExecuteAsync($@"
            UPDATE CARD_REFRESH_TOKENS SET REVOKED_AT = {Now}, REVOKE_REASON = :Reason
            WHERE USER_ID = :UserId AND REVOKED_AT IS NULL",
            new { UserId = userId, Reason = reason });
    }
}
