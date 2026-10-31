using Dapper;
using Oracle.ManagedDataAccess.Client;
using CardScout.Core.Models;

namespace CardScout.Core.Data;

public class EbayListingsRepository
{
    private readonly string _connString;

    public EbayListingsRepository(string connectionString)
    {
        _connString = connectionString;
    }

    public async Task<bool> InsertIfNewAsync(EbayListingRecord listing)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();

        const string checkSql = "SELECT COUNT(*) FROM EBAY_LISTINGS WHERE ITEM_ID = :ItemId";
        var exists = await conn.ExecuteScalarAsync<int>(checkSql, new { listing.ItemId });
        if (exists > 0) return false;

        const string insertSql = @"
            INSERT INTO EBAY_LISTINGS 
                (ITEM_ID, TITLE, CURRENT_PRICE, CONDITION, END_TIME, LISTING_URL, FIRST_SEEN, MATCH_STATUS, LISTING_TYPE, SHIPPING_COST, CATEGORY, GAME)
            VALUES 
                (:ItemId, :Title, :CurrentPrice, :Condition, :EndTime, :ListingUrl, SYSTIMESTAMP, 'PENDING', :ListingType, :ShippingCost, :Category, :Game)";
        await conn.ExecuteAsync(insertSql, listing);
        return true;
    }
    public async Task<List<string>> GetActiveDealItemIdsAsync()
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = @"
        SELECT DISTINCT e.ITEM_ID
        FROM EBAY_LISTINGS e
        JOIN DEALS d ON d.ITEM_ID = e.ITEM_ID
        WHERE e.MATCH_STATUS != 'SOLD'";
        var results = await conn.QueryAsync<string>(sql);
        return results.ToList();
    }

    public async Task MarkSoldAsync(string itemId)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = "UPDATE EBAY_LISTINGS SET MATCH_STATUS = 'SOLD' WHERE ITEM_ID = :ItemId";
        await conn.ExecuteAsync(sql, new { ItemId = itemId });
    }
    public async Task<List<EbayListingRecord>> GetPendingAsync(int limit = 50)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = @"
            SELECT * FROM (
                SELECT 
                    ITEM_ID AS ItemId, TITLE AS Title, CURRENT_PRICE AS CurrentPrice,
                    CONDITION AS Condition, END_TIME AS EndTime, LISTING_URL AS ListingUrl,
                    FIRST_SEEN AS FirstSeen, MATCH_STATUS AS MatchStatus, LISTING_TYPE AS ListingType,
                    SHIPPING_COST AS ShippingCost, CATEGORY AS Category, GAME AS Game
                FROM EBAY_LISTINGS
                WHERE MATCH_STATUS = 'PENDING'
                ORDER BY FIRST_SEEN ASC
            ) WHERE ROWNUM <= :Limit";
        var results = await conn.QueryAsync<EbayListingRecord>(sql, new { Limit = limit });
        return results.ToList();
    }
    public async Task UpdateShippingCostAsync(string itemId, decimal shippingCost)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = "UPDATE EBAY_LISTINGS SET SHIPPING_COST = :ShippingCost WHERE ITEM_ID = :ItemId";
        await conn.ExecuteAsync(sql, new { ShippingCost = shippingCost, ItemId = itemId });
    }
    public async Task UpdateMatchStatusAsync(string itemId, string status)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = "UPDATE EBAY_LISTINGS SET MATCH_STATUS = :Status WHERE ITEM_ID = :ItemId";
        await conn.ExecuteAsync(sql, new { Status = status, ItemId = itemId });
    }
}