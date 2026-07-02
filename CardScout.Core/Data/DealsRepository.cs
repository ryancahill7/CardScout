using Dapper;
using Oracle.ManagedDataAccess.Client;
using CardScout.Core.Models;

namespace CardScout.Core.Data;

public class DealsRepository
{
    private readonly string _connString;

    public DealsRepository(string connectionString)
    {
        _connString = connectionString;
    }

    public async Task InsertDealAsync(DealRecord deal)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = @"
            INSERT INTO DEALS (ITEM_ID, COMP_PRODUCT_ID, COMP_PRICE_USED, DISCOUNT_PCT, MATCH_CONFIDENCE, CREATED_AT)
            VALUES (:ItemId, :CompProductId, :CompPriceUsed, :DiscountPct, :MatchConfidence, SYSTIMESTAMP)";
        await conn.ExecuteAsync(sql, deal);
    }

    public async Task<List<DealRecord>> GetRecentDealsAsync(int limit = 50)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = @"
            SELECT * FROM (
                SELECT 
                    ITEM_ID AS ItemId, COMP_PRODUCT_ID AS CompProductId, COMP_PRICE_USED AS CompPriceUsed,
                    DISCOUNT_PCT AS DiscountPct, MATCH_CONFIDENCE AS MatchConfidence, CREATED_AT AS CreatedAt
                FROM DEALS
                ORDER BY DISCOUNT_PCT DESC
            ) WHERE ROWNUM <= :Limit";
        var results = await conn.QueryAsync<DealRecord>(sql, new { Limit = limit });
        return results.ToList();
    }
    public async Task<List<DealDetail>> GetDealsDetailAsync()
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = @"
    SELECT 
        ITEM_ID AS ItemId, EBAY_TITLE AS EbayTitle, LISTING_TYPE AS ListingType,
        EBAY_PRICE AS EbayPrice, SHIPPING_COST AS ShippingCost, EBAY_CONDITION AS EbayCondition,
        AUCTION_END_TIME_LOCAL AS AuctionEndTimeLocal, EBAY_URL AS EbayUrl, COMP_CARD_NAME AS CompCardName,
        COMP_SET_NAME AS CompSetName, COMP_URL AS CompUrl, COMP_PRICE AS CompPrice, DISCOUNT_PCT AS DiscountPct,
        MATCH_CONFIDENCE AS MatchConfidence, DEAL_FOUND_AT AS DealFoundAt
    FROM V_DEALS_DETAIL
    WHERE (AUCTION_END_TIME_LOCAL IS NULL OR AUCTION_END_TIME_LOCAL > SYSTIMESTAMP AT TIME ZONE 'America/Chicago')
    ORDER BY DEAL_FOUND_AT DESC";
        var results = await conn.QueryAsync<DealDetail>(sql);
        return results.ToList();
    }

    public async Task UpdateConfidenceAsync(string itemId, decimal matchConfidence)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = "UPDATE DEALS SET MATCH_CONFIDENCE = :MatchConfidence WHERE ITEM_ID = :ItemId";
        await conn.ExecuteAsync(sql, new { MatchConfidence = matchConfidence, ItemId = itemId });
    }
}