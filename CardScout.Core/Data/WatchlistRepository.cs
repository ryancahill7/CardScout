using System.Data;
using Dapper;
using Oracle.ManagedDataAccess.Client;
using CardScout.Core.Models;

namespace CardScout.Core.Data;

// Watchlists and the listings they've found. Like inventory, every method is scoped to a user.
public class WatchlistRepository
{
    private readonly string _connString;

    private const string WatchlistColumns = @"
        ID AS Id, USER_ID AS UserId, NAME AS Name, QUERY AS Query, LISTING_TYPE AS ListingType,
        MAX_PRICE AS MaxPrice, MIN_PROFIT AS MinProfit, MIN_PROFIT_PCT AS MinProfitPct,
        GRADED_ONLY AS GradedOnly, NOTIFY AS Notify, IS_ACTIVE AS IsActive, LAST_RUN_AT AS LastRunAt,
        CREATED_AT AS CreatedAt";

    private const string HitColumns = @"
        h.ID AS Id, h.WATCHLIST_ID AS WatchlistId, h.USER_ID AS UserId, w.NAME AS WatchlistName,
        h.ITEM_ID AS ItemId, h.TITLE AS Title, h.LISTING_TYPE AS ListingType, h.PRICE AS Price,
        h.SHIPPING_COST AS ShippingCost, h.SHIPPING_ESTIMATED AS ShippingEstimated, h.END_TIME AS EndTime,
        h.LISTING_URL AS ListingUrl, h.IMAGE_URL AS ImageUrl, h.COMP_PRODUCT_ID AS CompProductId,
        h.COMP_PRODUCT_NAME AS CompProductName, h.COMP_SET_NAME AS CompSetName, h.COMP_PRICE AS CompPrice,
        h.COMP_TIER AS CompTier, h.MATCH_CONFIDENCE AS MatchConfidence, h.NET_PROFIT AS NetProfit,
        h.NET_PROFIT_PCT AS NetProfitPct, h.MAX_BUY_PRICE AS MaxBuyPrice, h.IS_PROFITABLE AS IsProfitable,
        h.USER_STATUS AS UserStatus, h.NOTIFIED_AT AS NotifiedAt, h.CREATED_AT AS CreatedAt";

    public WatchlistRepository(string connectionString)
    {
        _connString = connectionString;
    }

    public async Task<List<Watchlist>> GetAllAsync(long userId, bool activeOnly = false)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var sql = $"SELECT {WatchlistColumns} FROM CARD_WATCHLISTS WHERE USER_ID = :UserId";
        if (activeOnly) sql += " AND IS_ACTIVE = 'Y'";
        sql += " ORDER BY CREATED_AT DESC FETCH FIRST 100 ROWS ONLY";
        var results = await conn.QueryAsync<Watchlist>(sql, new { UserId = userId });
        return results.ToList();
    }

    public async Task<Watchlist?> GetByIdAsync(long userId, long id)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var sql = $"SELECT {WatchlistColumns} FROM CARD_WATCHLISTS WHERE ID = :Id AND USER_ID = :UserId";
        return await conn.QuerySingleOrDefaultAsync<Watchlist>(sql, new { Id = id, UserId = userId });
    }

    public async Task<long> InsertAsync(Watchlist w)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = @"
            INSERT INTO CARD_WATCHLISTS
                (USER_ID, NAME, QUERY, LISTING_TYPE, MAX_PRICE, MIN_PROFIT, MIN_PROFIT_PCT, GRADED_ONLY, NOTIFY, IS_ACTIVE, CREATED_AT)
            VALUES
                (:UserId, :Name, :Query, :ListingType, :MaxPrice, :MinProfit, :MinProfitPct, :GradedOnly, :Notify, :IsActive, SYSTIMESTAMP)
            RETURNING ID INTO :NewId";
        var parameters = new DynamicParameters(new
        {
            w.UserId, w.Name, w.Query, w.ListingType, w.MaxPrice, w.MinProfit, w.MinProfitPct, w.GradedOnly, w.Notify, w.IsActive
        });
        parameters.Add("NewId", dbType: DbType.Int64, direction: ParameterDirection.Output);
        await conn.ExecuteAsync(sql, parameters);
        return parameters.Get<long>("NewId");
    }

    public async Task UpdateAsync(Watchlist w)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = @"
            UPDATE CARD_WATCHLISTS SET
                NAME = :Name, QUERY = :Query, LISTING_TYPE = :ListingType, MAX_PRICE = :MaxPrice,
                MIN_PROFIT = :MinProfit, MIN_PROFIT_PCT = :MinProfitPct, GRADED_ONLY = :GradedOnly,
                NOTIFY = :Notify, IS_ACTIVE = :IsActive
            WHERE ID = :Id AND USER_ID = :UserId";
        await conn.ExecuteAsync(sql, new
        {
            w.Name, w.Query, w.ListingType, w.MaxPrice, w.MinProfit, w.MinProfitPct, w.GradedOnly, w.Notify, w.IsActive,
            w.Id, w.UserId
        });
    }

    // Hits go with it (ON DELETE CASCADE)
    public async Task<bool> DeleteAsync(long userId, long id)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var rows = await conn.ExecuteAsync("DELETE FROM CARD_WATCHLISTS WHERE ID = :Id AND USER_ID = :UserId",
            new { Id = id, UserId = userId });
        return rows > 0;
    }

    public async Task MarkRunAsync(long userId, long id)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("UPDATE CARD_WATCHLISTS SET LAST_RUN_AT = SYSTIMESTAMP WHERE ID = :Id AND USER_ID = :UserId",
            new { Id = id, UserId = userId });
    }

    // Which of these eBay item ids this watchlist has already evaluated
    public async Task<HashSet<string>> GetSeenItemIdsAsync(long userId, long watchlistId, IReadOnlyCollection<string> itemIds)
    {
        if (itemIds.Count == 0) return [];
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = @"
            SELECT ITEM_ID FROM WATCHLIST_HITS
            WHERE USER_ID = :UserId AND WATCHLIST_ID = :WatchlistId AND ITEM_ID IN :ItemIds";
        var seen = await conn.QueryAsync<string>(sql, new { UserId = userId, WatchlistId = watchlistId, ItemIds = itemIds.ToArray() });
        return seen.ToHashSet();
    }

    public async Task<long> InsertHitAsync(WatchlistHit h)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = @"
            INSERT INTO WATCHLIST_HITS
                (WATCHLIST_ID, USER_ID, ITEM_ID, TITLE, LISTING_TYPE, PRICE, SHIPPING_COST, SHIPPING_ESTIMATED, END_TIME,
                 LISTING_URL, IMAGE_URL, COMP_PRODUCT_ID, COMP_PRODUCT_NAME, COMP_SET_NAME, COMP_PRICE, COMP_TIER,
                 MATCH_CONFIDENCE, NET_PROFIT, NET_PROFIT_PCT, MAX_BUY_PRICE, IS_PROFITABLE, USER_STATUS, CREATED_AT)
            VALUES
                (:WatchlistId, :UserId, :ItemId, :Title, :ListingType, :Price, :ShippingCost, :ShippingEstimated, :EndTime,
                 :ListingUrl, :ImageUrl, :CompProductId, :CompProductName, :CompSetName, :CompPrice, :CompTier,
                 :MatchConfidence, :NetProfit, :NetProfitPct, :MaxBuyPrice, :IsProfitable, 'NEW', SYSTIMESTAMP)
            RETURNING ID INTO :NewId";
        var parameters = new DynamicParameters(new
        {
            h.WatchlistId, h.UserId, h.ItemId, Title = Truncate(h.Title, 300), h.ListingType, h.Price, h.ShippingCost,
            h.ShippingEstimated, h.EndTime, ListingUrl = Truncate(h.ListingUrl, 1000), ImageUrl = Truncate(h.ImageUrl, 1000),
            h.CompProductId, CompProductName = Truncate(h.CompProductName, 300), CompSetName = Truncate(h.CompSetName, 300),
            h.CompPrice, h.CompTier, h.MatchConfidence, h.NetProfit, h.NetProfitPct, h.MaxBuyPrice, h.IsProfitable
        });
        parameters.Add("NewId", dbType: DbType.Int64, direction: ParameterDirection.Output);
        await conn.ExecuteAsync(sql, parameters);
        return parameters.Get<long>("NewId");
    }

    public async Task MarkNotifiedAsync(long userId, IReadOnlyCollection<long> hitIds)
    {
        if (hitIds.Count == 0) return;
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("UPDATE WATCHLIST_HITS SET NOTIFIED_AT = SYSTIMESTAMP WHERE USER_ID = :UserId AND ID IN :Ids",
            new { UserId = userId, Ids = hitIds.ToArray() });
    }

    // Profitable hits by default; auctions that already ended are left out
    public async Task<List<WatchlistHit>> GetHitsAsync(
        long userId, string? status = null, bool profitableOnly = true, long? watchlistId = null, int limit = 200)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var sql = $@"
            SELECT {HitColumns}
            FROM WATCHLIST_HITS h JOIN CARD_WATCHLISTS w ON w.ID = h.WATCHLIST_ID
            WHERE h.USER_ID = :UserId
              AND (h.END_TIME IS NULL OR h.END_TIME > SYS_EXTRACT_UTC(SYSTIMESTAMP))";
        if (profitableOnly) sql += " AND h.IS_PROFITABLE = 'Y'";
        if (status != null) sql += " AND h.USER_STATUS = :Status";
        else sql += " AND h.USER_STATUS != 'DISMISSED'";
        if (watchlistId != null) sql += " AND h.WATCHLIST_ID = :WatchlistId";
        sql += " ORDER BY h.CREATED_AT DESC FETCH FIRST :Limit ROWS ONLY";

        var results = await conn.QueryAsync<WatchlistHit>(sql,
            new { UserId = userId, Status = status, WatchlistId = watchlistId, Limit = limit });
        return results.ToList();
    }

    public async Task<WatchlistHit?> GetHitAsync(long userId, long id)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var sql = $@"
            SELECT {HitColumns}
            FROM WATCHLIST_HITS h JOIN CARD_WATCHLISTS w ON w.ID = h.WATCHLIST_ID
            WHERE h.ID = :Id AND h.USER_ID = :UserId";
        return await conn.QuerySingleOrDefaultAsync<WatchlistHit>(sql, new { Id = id, UserId = userId });
    }

    public async Task<bool> UpdateHitStatusAsync(long userId, long id, string status)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var rows = await conn.ExecuteAsync("UPDATE WATCHLIST_HITS SET USER_STATUS = :Status WHERE ID = :Id AND USER_ID = :UserId",
            new { Status = status, Id = id, UserId = userId });
        return rows > 0;
    }

    private static string? Truncate(string? s, int max) => s == null || s.Length <= max ? s : s[..max];
}
