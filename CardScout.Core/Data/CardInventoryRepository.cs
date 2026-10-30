using System.Data;
using Dapper;
using Oracle.ManagedDataAccess.Client;
using CardScout.Core.Models;

namespace CardScout.Core.Data;

public class CardInventoryRepository
{
    private readonly string _connString;

    private const string SelectColumns = @"
        ID AS Id, USER_ID AS UserId, PLAYER AS Player, CARD_YEAR AS CardYear, MANUFACTURER AS Manufacturer,
        SET_NAME AS SetName, CARD_NUMBER AS CardNumber, PARALLEL AS Parallel, SERIAL_NUMBER AS SerialNumber,
        IS_ROOKIE AS IsRookie, IS_AUTOGRAPH AS IsAutograph, IS_RELIC AS IsRelic,
        GRADING_COMPANY AS GradingCompany, GRADE AS Grade, CONDITION_NOTES AS ConditionNotes,
        AI_CONFIDENCE AS AiConfidence, EBAY_TITLE AS EbayTitle, SUGGESTED_PRICE AS SuggestedPrice,
        COMP_PRODUCT_ID AS CompProductId, COST_BASIS AS CostBasis, STATUS AS Status,
        LISTED_PRICE AS ListedPrice, SOLD_PRICE AS SoldPrice, SOLD_AT AS SoldAt,
        FRONT_IMAGE_PATH AS FrontImagePath, BACK_IMAGE_PATH AS BackImagePath,
        CREATED_AT AS CreatedAt, UPDATED_AT AS UpdatedAt";

    public CardInventoryRepository(string connectionString)
    {
        _connString = connectionString;
    }

    public async Task<long> InsertAsync(CardInventoryItem item)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();

        const string sql = @"
            INSERT INTO CARD_INVENTORY
                (USER_ID, PLAYER, CARD_YEAR, MANUFACTURER, SET_NAME, CARD_NUMBER, PARALLEL, SERIAL_NUMBER,
                 IS_ROOKIE, IS_AUTOGRAPH, IS_RELIC, GRADING_COMPANY, GRADE, CONDITION_NOTES,
                 AI_CONFIDENCE, EBAY_TITLE, SUGGESTED_PRICE, COMP_PRODUCT_ID, COST_BASIS, STATUS,
                 LISTED_PRICE, SOLD_PRICE, SOLD_AT, FRONT_IMAGE_PATH, BACK_IMAGE_PATH, AI_RAW_JSON,
                 CREATED_AT, UPDATED_AT)
            VALUES
                (:UserId, :Player, :CardYear, :Manufacturer, :SetName, :CardNumber, :Parallel, :SerialNumber,
                 :IsRookie, :IsAutograph, :IsRelic, :GradingCompany, :Grade, :ConditionNotes,
                 :AiConfidence, :EbayTitle, :SuggestedPrice, :CompProductId, :CostBasis, :Status,
                 :ListedPrice, :SoldPrice, :SoldAt, :FrontImagePath, :BackImagePath, :AiRawJson,
                 SYSTIMESTAMP, SYSTIMESTAMP)
            RETURNING ID INTO :NewId";

        var parameters = new DynamicParameters(item);
        parameters.Add("NewId", dbType: DbType.Int64, direction: ParameterDirection.Output);
        await conn.ExecuteAsync(sql, parameters);
        return parameters.Get<long>("NewId");
    }

    // Every read and write is scoped to a user; there is deliberately no unscoped lookup
    public async Task<List<CardInventoryItem>> GetAllAsync(long userId, string? status = null, int limit = 500)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();

        var sql = $"SELECT {SelectColumns} FROM CARD_INVENTORY WHERE USER_ID = :UserId";
        if (status != null) sql += " AND STATUS = :Status";
        sql += " ORDER BY CREATED_AT DESC FETCH FIRST :Limit ROWS ONLY";

        var results = await conn.QueryAsync<CardInventoryItem>(sql, new { UserId = userId, Status = status, Limit = limit });
        return results.ToList();
    }

    public async Task<CardInventoryItem?> GetByIdAsync(long userId, long id)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var sql = $"SELECT {SelectColumns} FROM CARD_INVENTORY WHERE ID = :Id AND USER_ID = :UserId";
        return await conn.QuerySingleOrDefaultAsync<CardInventoryItem>(sql, new { Id = id, UserId = userId });
    }

    public async Task UpdateAsync(CardInventoryItem item)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();

        const string sql = @"
            UPDATE CARD_INVENTORY SET
                PLAYER = :Player, CARD_YEAR = :CardYear, MANUFACTURER = :Manufacturer, SET_NAME = :SetName,
                CARD_NUMBER = :CardNumber, PARALLEL = :Parallel, SERIAL_NUMBER = :SerialNumber,
                IS_ROOKIE = :IsRookie, IS_AUTOGRAPH = :IsAutograph, IS_RELIC = :IsRelic,
                GRADING_COMPANY = :GradingCompany, GRADE = :Grade, CONDITION_NOTES = :ConditionNotes,
                EBAY_TITLE = :EbayTitle, SUGGESTED_PRICE = :SuggestedPrice, COMP_PRODUCT_ID = :CompProductId, COST_BASIS = :CostBasis,
                STATUS = :Status, LISTED_PRICE = :ListedPrice, SOLD_PRICE = :SoldPrice, SOLD_AT = :SoldAt,
                UPDATED_AT = SYSTIMESTAMP
            WHERE ID = :Id AND USER_ID = :UserId";
        await conn.ExecuteAsync(sql, new
        {
            item.Player, item.CardYear, item.Manufacturer, item.SetName, item.CardNumber, item.Parallel,
            item.SerialNumber, item.IsRookie, item.IsAutograph, item.IsRelic, item.GradingCompany, item.Grade,
            item.ConditionNotes, item.EbayTitle, item.SuggestedPrice, item.CompProductId, item.CostBasis, item.Status,
            item.ListedPrice, item.SoldPrice, item.SoldAt, item.Id, item.UserId
        });
    }
}
