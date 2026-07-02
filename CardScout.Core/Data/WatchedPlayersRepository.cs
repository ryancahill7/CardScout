using Dapper;
using Oracle.ManagedDataAccess.Client;

namespace CardScout.Core.Data;

public class WatchedPlayersRepository
{
    private readonly string _connString;

    public WatchedPlayersRepository(string connectionString)
    {
        _connString = connectionString;
    }

    public async Task<List<string>> GetActivePlayerNamesAsync()
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = "SELECT PLAYER_NAME FROM WATCHED_PLAYERS WHERE ACTIVE = 'Y' ORDER BY PLAYER_NAME";
        var results = await conn.QueryAsync<string>(sql);
        return results.ToList();
    }
}