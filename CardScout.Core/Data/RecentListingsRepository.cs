using Dapper;
using Oracle.ManagedDataAccess.Client;
using CardScout.Core.Models;

namespace CardScout.Core.Data;

public class RecentListingsRepository
{
    private readonly string _connString;

    public RecentListingsRepository(string connectionString)
    {
        _connString = connectionString;
    }

    public async Task<bool> InsertIfNewAsync(RecentListing listing)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();

        const string checkSql = "SELECT COUNT(*) FROM RECENT_LISTINGS WHERE ITEM_ID = :ItemId";
        var exists = await conn.ExecuteScalarAsync<int>(checkSql, new { listing.ItemId });
        if (exists > 0) return false;

        const string insertSql = @"
            INSERT INTO RECENT_LISTINGS 
                (ITEM_ID, PLAYER_NAME, TITLE, PRICE, LISTING_TYPE, CONDITION, LISTING_URL, FIRST_SEEN)
            VALUES 
                (:ItemId, :PlayerName, :Title, :Price, :ListingType, :Condition, :ListingUrl, SYSTIMESTAMP)";
        await conn.ExecuteAsync(insertSql, listing);
        return true;
    }

    public async Task<List<RecentListing>> GetRecentAsync(int limit = 100)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = @"
            SELECT * FROM (
                SELECT 
                    ITEM_ID AS ItemId, PLAYER_NAME AS PlayerName, TITLE AS Title,
                    PRICE AS Price, LISTING_TYPE AS ListingType, CONDITION AS Condition,
                    LISTING_URL AS ListingUrl, FIRST_SEEN AS FirstSeen
                FROM RECENT_LISTINGS
                ORDER BY FIRST_SEEN DESC
            ) WHERE ROWNUM <= :Limit";
        var results = await conn.QueryAsync<RecentListing>(sql, new { Limit = limit });
        return results.ToList();
    }
}