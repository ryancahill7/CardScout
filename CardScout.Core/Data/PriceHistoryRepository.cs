using Dapper;
using Oracle.ManagedDataAccess.Client;
using CardScout.Core.Models;

namespace CardScout.Core.Data;

// Market price snapshots for cards a user holds, written by PortfolioPriceWorker
public class PriceHistoryRepository
{
    private readonly string _connString;

    public PriceHistoryRepository(string connectionString)
    {
        _connString = connectionString;
    }

    public async Task InsertAsync(long userId, long inventoryId, decimal price)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO CARD_PRICE_HISTORY (USER_ID, INVENTORY_ID, PRICE, CAPTURED_AT)
            VALUES (:UserId, :InventoryId, :Price, SYSTIMESTAMP)",
            new { UserId = userId, InventoryId = inventoryId, Price = price });
    }

    public async Task<List<PriceHistoryPoint>> GetForCardAsync(long userId, long inventoryId, int days = 180)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var points = await conn.QueryAsync<PriceHistoryPoint>(@"
            SELECT INVENTORY_ID AS InventoryId, PRICE AS Price, CAPTURED_AT AS CapturedAt
            FROM CARD_PRICE_HISTORY
            WHERE USER_ID = :UserId AND INVENTORY_ID = :InventoryId AND CAPTURED_AT > :Since
            ORDER BY CAPTURED_AT
            FETCH FIRST 500 ROWS ONLY",
            new { UserId = userId, InventoryId = inventoryId, Since = DateTime.Now.AddDays(-days) });
        return points.ToList();
    }

    // Every snapshot for the user's cards in the window, for portfolio movers
    public async Task<List<PriceHistoryPoint>> GetForUserAsync(long userId, int days = 30)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var points = await conn.QueryAsync<PriceHistoryPoint>(@"
            SELECT INVENTORY_ID AS InventoryId, PRICE AS Price, CAPTURED_AT AS CapturedAt
            FROM CARD_PRICE_HISTORY
            WHERE USER_ID = :UserId AND CAPTURED_AT > :Since
            ORDER BY INVENTORY_ID, CAPTURED_AT
            FETCH FIRST 5000 ROWS ONLY",
            new { UserId = userId, Since = DateTime.Now.AddDays(-days) });
        return points.ToList();
    }
}
