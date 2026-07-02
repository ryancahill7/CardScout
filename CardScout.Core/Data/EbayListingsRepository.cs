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
                (ITEM_ID, TITLE, CURRENT_PRICE, CONDITION, END_TIME, LISTING_URL, FIRST_SEEN, MATCH_STATUS, LISTING_TYPE, SHIPPING_COST)
            VALUES 
                (:ItemId, :Title, :CurrentPrice, :Condition, :EndTime, :ListingUrl, SYSTIMESTAMP, 'PENDING', :ListingType, :ShippingCost)";
        await conn.ExecuteAsync(insertSql, listing);
        return true;
    }

    public async Task<List<EbayListingRecord>> GetPendingAsync(int limit = 10)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = @"
            SELECT * FROM (
                SELECT 
                    ITEM_ID AS ItemId, TITLE AS Title, CURRENT_PRICE AS CurrentPrice,
                    CONDITION AS Condition, END_TIME AS EndTime, LISTING_URL AS ListingUrl,
                    FIRST_SEEN AS FirstSeen, MATCH_STATUS AS MatchStatus, LISTING_TYPE AS ListingType,
                    SHIPPING_COST AS ShippingCost
                FROM EBAY_LISTINGS
                WHERE MATCH_STATUS = 'PENDING'
                ORDER BY FIRST_SEEN ASC
            ) WHERE ROWNUM <= :Limit";
        var results = await conn.QueryAsync<EbayListingRecord>(sql, new { Limit = limit });
        return results.ToList();
    }

    public async Task UpdateMatchStatusAsync(string itemId, string status)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = "UPDATE EBAY_LISTINGS SET MATCH_STATUS = :Status WHERE ITEM_ID = :ItemId";
        await conn.ExecuteAsync(sql, new { Status = status, ItemId = itemId });
    }
}