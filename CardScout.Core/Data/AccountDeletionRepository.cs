using Dapper;
using Oracle.ManagedDataAccess.Client;

namespace CardScout.Core.Data;

// Deletes one user and everything they own, in one transaction (App Store account deletion).
// Images under users/{id}/ are removed separately through IImageStorage.
public class AccountDeletionRepository
{
    // Child tables first so foreign keys never block. Every table with a USER_ID that points at
    // CARD_USERS must be listed here: a missing one makes the final CARD_USERS delete fail with
    // ORA-02292 and the whole transaction rolls back, so nothing is half-deleted.
    public static readonly IReadOnlyList<string> UserTables =
    [
        "CARD_PRICE_HISTORY",
        "WATCHLIST_HITS",
        "CARD_WATCHLISTS",
        "CARD_INVENTORY",
        "CARD_PUSH_TOKENS",
        "CARD_REFRESH_TOKENS",
        "CARD_PASSWORD_RESETS",
        "CARD_AUTH_FLOWS",
        "CARD_USER_IDENTITIES",
    ];

    private readonly string _connString;

    public AccountDeletionRepository(string connectionString)
    {
        _connString = connectionString;
    }

    // Returns the rows deleted per table (for the log)
    public async Task<Dictionary<string, int>> DeleteUserAsync(long userId)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        var counts = new Dictionary<string, int>();
        foreach (var table in UserTables)
            counts[table] = await conn.ExecuteAsync($"DELETE FROM {table} WHERE USER_ID = :UserId", new { UserId = userId }, tx);

        counts["CARD_USERS"] = await conn.ExecuteAsync("DELETE FROM CARD_USERS WHERE ID = :UserId", new { UserId = userId }, tx);
        tx.Commit();
        return counts;
    }
}
