using Dapper;
using Oracle.ManagedDataAccess.Client;

namespace CardScout.Core.Data;

// CARD_PASSWORD_RESETS: hashed, single-use reset tokens. Times are UTC.
public class PasswordResetRepository
{
    private const string Now = "SYS_EXTRACT_UTC(SYSTIMESTAMP)";

    private readonly string _connString;

    public PasswordResetRepository(string connectionString)
    {
        _connString = connectionString;
    }

    // A new link replaces any earlier unused one, so only the latest email works
    public async Task CreateAsync(long userId, string tokenHash, TimeSpan lifetime)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync($"UPDATE CARD_PASSWORD_RESETS SET USED_AT = {Now} WHERE USER_ID = :UserId AND USED_AT IS NULL",
            new { UserId = userId }, tx);
        await conn.ExecuteAsync($@"
            INSERT INTO CARD_PASSWORD_RESETS (USER_ID, TOKEN_HASH, CREATED_AT, EXPIRES_AT)
            VALUES (:UserId, :TokenHash, {Now}, {Now} + NUMTODSINTERVAL(:Seconds, 'SECOND'))",
            new { UserId = userId, TokenHash = tokenHash, Seconds = (long)lifetime.TotalSeconds }, tx);
        tx.Commit();
    }

    // How many links were requested for the user recently (to cap reset emails per account)
    public async Task<int> CountRecentAsync(long userId, TimeSpan window)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>($@"
            SELECT COUNT(*) FROM CARD_PASSWORD_RESETS
            WHERE USER_ID = :UserId AND CREATED_AT > {Now} - NUMTODSINTERVAL(:Seconds, 'SECOND')",
            new { UserId = userId, Seconds = (long)window.TotalSeconds });
    }

    // Marks a live token used and returns its user, atomically; null if unknown, used or expired
    public async Task<long?> ConsumeAsync(string tokenHash)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var p = new DynamicParameters(new { TokenHash = tokenHash });
        p.Add("UserId", dbType: System.Data.DbType.Int64, direction: System.Data.ParameterDirection.Output);
        var rows = await conn.ExecuteAsync($@"
            UPDATE CARD_PASSWORD_RESETS SET USED_AT = {Now}
            WHERE TOKEN_HASH = :TokenHash AND USED_AT IS NULL AND EXPIRES_AT > {Now}
            RETURNING USER_ID INTO :UserId", p);
        return rows == 1 ? p.Get<long>("UserId") : null;
    }
}
