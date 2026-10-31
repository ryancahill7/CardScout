using Dapper;
using Oracle.ManagedDataAccess.Client;
using CardScout.Core.Models;

namespace CardScout.Core.Data;

public class CompCacheRepository
{
    private readonly string _connString;

    public CompCacheRepository(string connectionString)
    {
        _connString = connectionString;
    }

    public async Task<CompCacheRecord?> GetCachedAsync(string searchQuery)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = @"
        SELECT 
            SEARCH_QUERY AS SearchQuery, PRODUCT_ID AS ProductId, PRODUCT_NAME AS ProductName,
            CONSOLE_NAME AS ConsoleName, LOOSE_PRICE AS LoosePrice, GRADED_PRICE AS GradedPrice,
            NEW_PRICE AS NewPrice, CIB_PRICE AS CibPrice, MANUAL_ONLY_PRICE AS ManualOnlyPrice,
            BOX_ONLY_PRICE AS BoxOnlyPrice, BGS_10_PRICE AS Bgs10Price,
            GRADE1_PRICE AS Grade1Price, GRADE2_PRICE AS Grade2Price, GRADE3_PRICE AS Grade3Price,
            GRADE4_PRICE AS Grade4Price, GRADE5_PRICE AS Grade5Price, GRADE6_PRICE AS Grade6Price,
            CGC10_PRICE AS Cgc10Price, SGC10_PRICE AS Sgc10Price, SALES_VOLUME AS SalesVolume,
            COMP_URL AS CompUrl, LAST_UPDATED AS LastUpdated
        FROM COMP_CACHE
        WHERE SEARCH_QUERY = :SearchQuery";
        return await conn.QueryFirstOrDefaultAsync<CompCacheRecord>(sql, new { SearchQuery = searchQuery });
    }

    public async Task UpsertAsync(CompCacheRecord record)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();

        const string checkSql = "SELECT COUNT(*) FROM COMP_CACHE WHERE SEARCH_QUERY = :SearchQuery";
        var exists = await conn.ExecuteScalarAsync<int>(checkSql, new { record.SearchQuery });

        if (exists > 0)
        {
            const string updateSql = @"
            UPDATE COMP_CACHE SET
                PRODUCT_ID = :ProductId, PRODUCT_NAME = :ProductName, CONSOLE_NAME = :ConsoleName,
                LOOSE_PRICE = :LoosePrice, GRADED_PRICE = :GradedPrice, NEW_PRICE = :NewPrice,
                CIB_PRICE = :CibPrice, MANUAL_ONLY_PRICE = :ManualOnlyPrice,
                BOX_ONLY_PRICE = :BoxOnlyPrice, BGS_10_PRICE = :Bgs10Price, COMP_URL = :CompUrl,
                GRADE1_PRICE = :Grade1Price, GRADE2_PRICE = :Grade2Price, GRADE3_PRICE = :Grade3Price,
                GRADE4_PRICE = :Grade4Price, GRADE5_PRICE = :Grade5Price, GRADE6_PRICE = :Grade6Price,
                CGC10_PRICE = :Cgc10Price, SGC10_PRICE = :Sgc10Price, SALES_VOLUME = :SalesVolume,
                LAST_UPDATED = SYSTIMESTAMP
            WHERE SEARCH_QUERY = :SearchQuery";
            await conn.ExecuteAsync(updateSql, record);
        }
        else
        {
            const string insertSql = @"
            INSERT INTO COMP_CACHE 
                (SEARCH_QUERY, PRODUCT_ID, PRODUCT_NAME, CONSOLE_NAME, LOOSE_PRICE, GRADED_PRICE,
                 NEW_PRICE, CIB_PRICE, MANUAL_ONLY_PRICE, BOX_ONLY_PRICE, BGS_10_PRICE, COMP_URL, LAST_UPDATED,
                 GRADE1_PRICE, GRADE2_PRICE, GRADE3_PRICE, GRADE4_PRICE, GRADE5_PRICE, GRADE6_PRICE,
                 CGC10_PRICE, SGC10_PRICE, SALES_VOLUME)
            VALUES 
                (:SearchQuery, :ProductId, :ProductName, :ConsoleName, :LoosePrice, :GradedPrice,
                 :NewPrice, :CibPrice, :ManualOnlyPrice, :BoxOnlyPrice, :Bgs10Price, :CompUrl, SYSTIMESTAMP,
                 :Grade1Price, :Grade2Price, :Grade3Price, :Grade4Price, :Grade5Price, :Grade6Price,
                 :Cgc10Price, :Sgc10Price, :SalesVolume)";
            await conn.ExecuteAsync(insertSql, record);
        }
    }
}