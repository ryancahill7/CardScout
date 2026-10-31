using System.Data;
using Dapper;
using Oracle.ManagedDataAccess.Client;
using CardScout.Core.Models;

namespace CardScout.Core.Data;

// CARD_AUTH_FLOWS: browser sign-in brokered by the API (start → provider → callback → app → complete).
// Times are UTC.
public class AuthFlowRepository
{
    private const string Now = "SYS_EXTRACT_UTC(SYSTIMESTAMP)";

    private const string Columns = @"
        ID AS Id, PROVIDER AS Provider, APP_REDIRECT_URI AS AppRedirectUri, APP_CODE_CHALLENGE AS AppCodeChallenge,
        PROVIDER_VERIFIER AS ProviderVerifier, NONCE AS Nonce, EXPIRES_AT AS ExpiresAt, CALLBACK_AT AS CallbackAt,
        USER_ID AS UserId, CODE_EXPIRES_AT AS CodeExpiresAt, CODE_USED_AT AS CodeUsedAt";

    private readonly string _connString;

    public AuthFlowRepository(string connectionString)
    {
        _connString = connectionString;
    }

    public async Task CreateAsync(string stateHash, string provider, string appRedirectUri, string appCodeChallenge,
        string? providerVerifier, string? nonce, TimeSpan lifetime)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        await conn.ExecuteAsync($@"
            INSERT INTO CARD_AUTH_FLOWS (STATE_HASH, PROVIDER, APP_REDIRECT_URI, APP_CODE_CHALLENGE, PROVIDER_VERIFIER, NONCE,
                                         CREATED_AT, EXPIRES_AT)
            VALUES (:StateHash, :Provider, :AppRedirectUri, :AppCodeChallenge, :ProviderVerifier, :Nonce,
                    {Now}, {Now} + NUMTODSINTERVAL(:Seconds, 'SECOND'))",
            new
            {
                StateHash = stateHash,
                Provider = provider,
                AppRedirectUri = appRedirectUri,
                AppCodeChallenge = appCodeChallenge,
                ProviderVerifier = providerVerifier,
                Nonce = nonce,
                Seconds = (long)lifetime.TotalSeconds,
            });
    }

    // Claims the flow for its one callback: unknown, expired or already-called-back states return null
    public async Task<AuthFlow?> ClaimCallbackAsync(string stateHash)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var claimed = await conn.ExecuteAsync($@"
            UPDATE CARD_AUTH_FLOWS SET CALLBACK_AT = {Now}
            WHERE STATE_HASH = :StateHash AND CALLBACK_AT IS NULL AND EXPIRES_AT > {Now}",
            new { StateHash = stateHash });
        if (claimed != 1) return null;
        return await conn.QuerySingleOrDefaultAsync<AuthFlow>(
            $"SELECT {Columns} FROM CARD_AUTH_FLOWS WHERE STATE_HASH = :StateHash", new { StateHash = stateHash });
    }

    // After the provider confirmed who the user is: a short-lived one-time code for the app
    public async Task SetResultAsync(long flowId, long userId, string codeHash, TimeSpan codeLifetime)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        await conn.ExecuteAsync($@"
            UPDATE CARD_AUTH_FLOWS SET USER_ID = :UserId, CODE_HASH = :CodeHash,
                CODE_EXPIRES_AT = {Now} + NUMTODSINTERVAL(:Seconds, 'SECOND'), PROVIDER_VERIFIER = NULL
            WHERE ID = :Id",
            new { Id = flowId, UserId = userId, CodeHash = codeHash, Seconds = (long)codeLifetime.TotalSeconds });
    }

    // Single use: marks the code used and returns the flow if it was live
    public async Task<AuthFlow?> ConsumeCodeAsync(string codeHash)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var used = await conn.ExecuteAsync($@"
            UPDATE CARD_AUTH_FLOWS SET CODE_USED_AT = {Now}
            WHERE CODE_HASH = :CodeHash AND CODE_USED_AT IS NULL AND CODE_EXPIRES_AT > {Now} AND USER_ID IS NOT NULL",
            new { CodeHash = codeHash });
        if (used != 1) return null;
        var flow = await conn.QuerySingleOrDefaultAsync<AuthFlow>(
            $"SELECT {Columns} FROM CARD_AUTH_FLOWS WHERE CODE_HASH = :CodeHash", new { CodeHash = codeHash });
        // Done with it: nothing in the row is needed once the app has its tokens
        if (flow != null)
            await conn.ExecuteAsync("DELETE FROM CARD_AUTH_FLOWS WHERE ID = :Id AND USER_ID = :UserId", new { flow.Id, flow.UserId });
        return flow;
    }

    // Housekeeping for abandoned sign-ins: only rows that never reached a user (no user data).
    // Finished flows are deleted by ConsumeCodeAsync, and by account deletion.
    public async Task<int> DeleteAbandonedAsync(TimeSpan olderThan)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        return await conn.ExecuteAsync(
            $"DELETE FROM CARD_AUTH_FLOWS WHERE USER_ID IS NULL AND CREATED_AT < {Now} - NUMTODSINTERVAL(:Seconds, 'SECOND')",
            new { Seconds = (long)olderThan.TotalSeconds });
    }
}
