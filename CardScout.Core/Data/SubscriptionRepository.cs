using Dapper;
using Oracle.ManagedDataAccess.Client;
using CardScout.Core.Billing;
using CardScout.Core.Models;

namespace CardScout.Core.Data;

// CARD_SUBSCRIPTIONS (one row per user) and CARD_STRIPE_EVENTS (processed webhook ids)
public class SubscriptionRepository
{
    private const string Columns = @"
        USER_ID AS UserId, STRIPE_CUSTOMER_ID AS StripeCustomerId, STRIPE_SUBSCRIPTION_ID AS StripeSubscriptionId,
        STRIPE_PRICE_ID AS StripePriceId, BILLING_INTERVAL AS BillingInterval, PLAN AS Plan, STATUS AS Status,
        CURRENT_PERIOD_END AS CurrentPeriodEnd, CANCEL_AT_PERIOD_END AS CancelAtPeriodEnd,
        LAST_PAYMENT_FAILED_AT AS LastPaymentFailedAt, CREATED_AT AS CreatedAt, UPDATED_AT AS UpdatedAt";

    private readonly string _connString;

    public SubscriptionRepository(string connectionString)
    {
        _connString = connectionString;
    }

    public async Task<CardSubscription?> GetAsync(long userId)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<CardSubscription>(
            $"SELECT {Columns} FROM CARD_SUBSCRIPTIONS WHERE USER_ID = :UserId", new { UserId = userId });
    }

    // Webhooks arrive without a user; this maps the Stripe customer back to the one user it was created for.
    // It returns only the id, which the caller then uses for user-scoped calls.
    public async Task<long?> FindUserIdByCustomerAsync(string stripeCustomerId)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<long?>(
            "SELECT USER_ID FROM CARD_SUBSCRIPTIONS WHERE STRIPE_CUSTOMER_ID = :CustomerId",
            new { CustomerId = stripeCustomerId });
    }

    // Name and email to put on the Stripe customer (so the dashboard and receipts are readable)
    public async Task<(string? Name, string? Email)> GetUserContactAsync(long userId)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var row = await conn.QuerySingleOrDefaultAsync<(string? Name, string? Email)>(
            "SELECT DISPLAY_NAME, EMAIL FROM CARD_USERS WHERE ID = :UserId", new { UserId = userId });
        return row;
    }

    // Called once, right after the Stripe customer is created for this user
    public async Task SetCustomerAsync(long userId, string stripeCustomerId)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = @"
            MERGE INTO CARD_SUBSCRIPTIONS t
            USING (SELECT :UserId AS USER_ID FROM DUAL) s ON (t.USER_ID = s.USER_ID)
            WHEN MATCHED THEN UPDATE SET t.STRIPE_CUSTOMER_ID = :CustomerId, t.UPDATED_AT = SYSTIMESTAMP
            WHEN NOT MATCHED THEN INSERT (USER_ID, STRIPE_CUSTOMER_ID, PLAN, CREATED_AT, UPDATED_AT)
                VALUES (:UserId, :CustomerId, 'FREE', SYSTIMESTAMP, SYSTIMESTAMP)";
        await conn.ExecuteAsync(sql, new { UserId = userId, CustomerId = stripeCustomerId });
    }

    // Writes the latest state of a subscription. A user can end up with two subscriptions (e.g. an
    // old canceled one); an update for a different subscription only wins if it's pro or the stored one isn't.
    public async Task UpsertSubscriptionAsync(long userId, SubscriptionSnapshot s)
    {
        var plan = PlanRules.IsProStatus(s.Status) ? "PRO" : "FREE";
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        const string sql = @"
            MERGE INTO CARD_SUBSCRIPTIONS t
            USING (SELECT :UserId AS USER_ID FROM DUAL) s ON (t.USER_ID = s.USER_ID)
            WHEN MATCHED THEN UPDATE SET
                t.STRIPE_CUSTOMER_ID = :CustomerId,
                t.STRIPE_SUBSCRIPTION_ID = :SubscriptionId,
                t.STRIPE_PRICE_ID = :PriceId,
                t.BILLING_INTERVAL = :Interval,
                t.PLAN = :Plan,
                t.STATUS = :Status,
                t.CURRENT_PERIOD_END = :CurrentPeriodEnd,
                t.CANCEL_AT_PERIOD_END = :CancelAtPeriodEnd,
                t.UPDATED_AT = SYSTIMESTAMP
                WHERE t.STRIPE_SUBSCRIPTION_ID IS NULL
                   OR t.STRIPE_SUBSCRIPTION_ID = :SubscriptionId
                   OR t.PLAN = 'FREE'
                   OR :Plan = 'PRO'
            WHEN NOT MATCHED THEN INSERT (USER_ID, STRIPE_CUSTOMER_ID, STRIPE_SUBSCRIPTION_ID, STRIPE_PRICE_ID,
                    BILLING_INTERVAL, PLAN, STATUS, CURRENT_PERIOD_END, CANCEL_AT_PERIOD_END, CREATED_AT, UPDATED_AT)
                VALUES (:UserId, :CustomerId, :SubscriptionId, :PriceId,
                    :Interval, :Plan, :Status, :CurrentPeriodEnd, :CancelAtPeriodEnd, SYSTIMESTAMP, SYSTIMESTAMP)";
        await conn.ExecuteAsync(sql, new
        {
            UserId = userId,
            CustomerId = s.StripeCustomerId,
            SubscriptionId = s.StripeSubscriptionId,
            s.PriceId,
            s.Interval,
            Plan = plan,
            s.Status,
            s.CurrentPeriodEnd,
            CancelAtPeriodEnd = s.CancelAtPeriodEnd ? "Y" : "N",
        });
    }

    public async Task MarkPaymentFailedAsync(long userId)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE CARD_SUBSCRIPTIONS SET LAST_PAYMENT_FAILED_AT = SYSTIMESTAMP, UPDATED_AT = SYSTIMESTAMP WHERE USER_ID = :UserId",
            new { UserId = userId });
    }

    public async Task<bool> IsEventProcessedAsync(string eventId)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        var n = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM CARD_STRIPE_EVENTS WHERE EVENT_ID = :EventId", new { EventId = eventId });
        return n > 0;
    }

    // Recorded after the event is handled; a concurrent duplicate delivery just hits the primary key
    public async Task MarkEventProcessedAsync(string eventId, string eventType, long? userId)
    {
        using var conn = new OracleConnection(_connString);
        await conn.OpenAsync();
        try
        {
            await conn.ExecuteAsync(
                "INSERT INTO CARD_STRIPE_EVENTS (EVENT_ID, EVENT_TYPE, USER_ID, PROCESSED_AT) VALUES (:EventId, :EventType, :UserId, SYSTIMESTAMP)",
                new { EventId = eventId, EventType = eventType, UserId = userId });
        }
        catch (OracleException ex) when (ex.Number == 1)
        {
            // ORA-00001: already recorded
        }
    }
}
