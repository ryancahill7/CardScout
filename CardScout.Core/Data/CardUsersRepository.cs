using System.Data;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Oracle.ManagedDataAccess.Client;
using CardScout.Core.Models;

namespace CardScout.Core.Data;

public class CardUsersRepository
{
    private readonly string _connString;

    public CardUsersRepository(string connectionString)
    {
        _connString = connectionString;
    }

    // Only the hash of an API key is stored, so a leaked table doesn't leak working keys
    public static string HashApiKey(string apiKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));

    public async Task<CardUser?> GetActiveByApiKeyAsync(string apiKey)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = @"
            SELECT ID AS Id, EMAIL AS Email, DISPLAY_NAME AS DisplayName, IS_ACTIVE AS IsActive, CREATED_AT AS CreatedAt
            FROM CARD_USERS
            WHERE API_KEY_HASH = :ApiKeyHash AND IS_ACTIVE = 'Y'";
        return await conn.QuerySingleOrDefaultAsync<CardUser>(sql, new { ApiKeyHash = HashApiKey(apiKey) });
    }

    public async Task<long> CreateAsync(string? displayName, string? email, string apiKey)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();

        const string sql = @"
            INSERT INTO CARD_USERS (DISPLAY_NAME, EMAIL, API_KEY_HASH, CREATED_AT)
            VALUES (:DisplayName, :Email, :ApiKeyHash, SYSTIMESTAMP)
            RETURNING ID INTO :NewId";

        var parameters = new DynamicParameters(new { DisplayName = displayName, Email = email, ApiKeyHash = HashApiKey(apiKey) });
        parameters.Add("NewId", dbType: DbType.Int64, direction: ParameterDirection.Output);
        await conn.ExecuteAsync(sql, parameters);
        return parameters.Get<long>("NewId");
    }

    // For background workers, which then make user-scoped calls per user
    public async Task<List<long>> GetActiveIdsAsync()
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var ids = await conn.QueryAsync<long>("SELECT ID FROM CARD_USERS WHERE IS_ACTIVE = 'Y' ORDER BY ID");
        return ids.ToList();
    }
}
