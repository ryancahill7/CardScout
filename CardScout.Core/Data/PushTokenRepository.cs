using Dapper;
using Oracle.ManagedDataAccess.Client;

namespace CardScout.Core.Data;

public class PushTokenRepository
{
    private readonly string _connString;

    public PushTokenRepository(string connectionString)
    {
        _connString = connectionString;
    }

    // A token belongs to one device; if it shows up under another user (shared phone, re-login) it moves
    public async Task UpsertAsync(long userId, string token, string? platform)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = @"
            MERGE INTO CARD_PUSH_TOKENS t
            USING (SELECT :Token AS TOKEN FROM DUAL) s ON (t.TOKEN = s.TOKEN)
            WHEN MATCHED THEN UPDATE SET t.USER_ID = :UserId, t.PLATFORM = :Platform, t.LAST_SEEN_AT = SYSTIMESTAMP
            WHEN NOT MATCHED THEN INSERT (USER_ID, TOKEN, PLATFORM, CREATED_AT, LAST_SEEN_AT)
                VALUES (:UserId, :Token, :Platform, SYSTIMESTAMP, SYSTIMESTAMP)";
        await conn.ExecuteAsync(sql, new { UserId = userId, Token = token, Platform = platform });
    }

    public async Task<List<string>> GetTokensAsync(long userId)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var tokens = await conn.QueryAsync<string>(
            "SELECT TOKEN FROM CARD_PUSH_TOKENS WHERE USER_ID = :UserId ORDER BY LAST_SEEN_AT DESC FETCH FIRST 10 ROWS ONLY",
            new { UserId = userId });
        return tokens.ToList();
    }

    public async Task DeleteAsync(long userId, string token)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM CARD_PUSH_TOKENS WHERE USER_ID = :UserId AND TOKEN = :Token",
            new { UserId = userId, Token = token });
    }
}
