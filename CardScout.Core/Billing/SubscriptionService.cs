using System.Collections.Concurrent;
using CardScout.Core.Data;
using CardScout.Core.Models;

namespace CardScout.Core.Billing;

public static class PlanRules
{
    public const string Free = "free";
    public const string Pro = "pro";

    // Stripe statuses that unlock Pro. past_due is deliberately not included: Stripe retries the
    // card for a while, and the user can fix it in the billing portal.
    public static bool IsProStatus(string? stripeStatus) => stripeStatus is "active" or "trialing";
}

// What other features (usage caps, paywall) need to know about a user's plan
public record UserPlan(string Plan, string? Status, DateTime? CurrentPeriodEnd, bool CancelAtPeriodEnd)
{
    public bool IsPro => Plan == PlanRules.Pro;

    public static readonly UserPlan Free = new(PlanRules.Free, null, null, false);
}

public interface ISubscriptionService
{
    // Never throws for a user without a subscription row: they're on the free plan
    Task<UserPlan> GetPlanAsync(long userId);

    Task<bool> IsProAsync(long userId);

    // Drop the cached plan after a webhook changes it
    void Invalidate(long userId);
}

// Reads CARD_SUBSCRIPTIONS, which the Stripe webhook keeps current. Plans are cached briefly
// because usage checks call this on every metered request.
public class SubscriptionService : ISubscriptionService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private readonly SubscriptionRepository _repo;
    private readonly ConcurrentDictionary<long, (UserPlan Plan, DateTime Expires)> _cache = new();

    public SubscriptionService(SubscriptionRepository repo)
    {
        _repo = repo;
    }

    public async Task<UserPlan> GetPlanAsync(long userId)
    {
        if (_cache.TryGetValue(userId, out var hit) && hit.Expires > DateTime.UtcNow)
            return hit.Plan;

        var plan = ToPlan(await _repo.GetAsync(userId));
        _cache[userId] = (plan, DateTime.UtcNow.Add(CacheDuration));
        return plan;
    }

    public async Task<bool> IsProAsync(long userId) => (await GetPlanAsync(userId)).IsPro;

    public void Invalidate(long userId) => _cache.TryRemove(userId, out _);

    public static UserPlan ToPlan(CardSubscription? row)
    {
        if (row?.StripeSubscriptionId == null) return UserPlan.Free;
        return new UserPlan(
            PlanRules.IsProStatus(row.Status) ? PlanRules.Pro : PlanRules.Free,
            row.Status,
            row.CurrentPeriodEnd,
            row.CancelAtPeriodEnd == "Y");
    }
}
