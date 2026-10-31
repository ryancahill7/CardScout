using Dapper;
using Oracle.ManagedDataAccess.Client;

namespace CardScout.Core.Data;

public record AiUsageRecord(
    long UserId,
    DateTime CreatedAtUtc,
    string Feature,
    string Model,
    long InputTokens,
    long OutputTokens,
    long CacheWriteTokens,
    long CacheReadTokens,
    decimal CostUsd,
    string Plan,
    bool Succeeded);

public record AiUsageSummary(decimal CostUsd, int Calls);

// CARD_AI_USAGE (one row per Claude call) and CARD_USAGE_ALERTS. Every query is user-scoped.
public class AiUsageRepository
{
    private readonly string _connString;

    public AiUsageRepository(string connectionString)
    {
        _connString = connectionString;
    }

    public async Task InsertAsync(AiUsageRecord r)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = @"
            INSERT INTO CARD_AI_USAGE (USER_ID, CREATED_AT, FEATURE, MODEL, INPUT_TOKENS, OUTPUT_TOKENS,
                CACHE_WRITE_TOKENS, CACHE_READ_TOKENS, COST_USD, PLAN, SUCCEEDED)
            VALUES (:UserId, :CreatedAt, :Feature, :Model, :InputTokens, :OutputTokens,
                :CacheWriteTokens, :CacheReadTokens, :CostUsd, :Plan, :Succeeded)";
        await conn.ExecuteAsync(sql, new
        {
            r.UserId,
            CreatedAt = r.CreatedAtUtc,
            r.Feature,
            r.Model,
            r.InputTokens,
            r.OutputTokens,
            r.CacheWriteTokens,
            r.CacheReadTokens,
            r.CostUsd,
            r.Plan,
            Succeeded = r.Succeeded ? "Y" : "N"
        });
    }

    // Spend and call count since monthStartUtc (inclusive)
    public async Task<AiUsageSummary> GetSummaryAsync(long userId, DateTime monthStartUtc)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = @"
            SELECT NVL(SUM(COST_USD), 0) AS CostUsd, COUNT(*) AS Calls
            FROM CARD_AI_USAGE
            WHERE USER_ID = :UserId AND CREATED_AT >= :MonthStart";
        var row = await conn.QuerySingleAsync<(decimal CostUsd, decimal Calls)>(sql, new { UserId = userId, MonthStart = monthStartUtc });
        return new AiUsageSummary(row.CostUsd, (int)row.Calls);
    }

    // Records that an alert went out this month. Returns false if it was already sent
    // (the primary key makes this safe when two requests cross the threshold together).
    public async Task<bool> TryMarkAlertAsync(long userId, DateTime monthStartUtc, string kind)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        try
        {
            await conn.ExecuteAsync(
                "INSERT INTO CARD_USAGE_ALERTS (USER_ID, MONTH_START, ALERT_KIND) VALUES (:UserId, :MonthStart, :Kind)",
                new { UserId = userId, MonthStart = monthStartUtc, Kind = kind });
            return true;
        }
        catch (OracleException ex) when (ex.Number == 1) // ORA-00001 unique constraint violated
        {
            return false;
        }
    }
}
