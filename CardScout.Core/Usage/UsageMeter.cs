using CardScout.Core.Billing;
using CardScout.Core.Data;
using CardScout.Core.Identify;
using CardScout.Core.Notifications;
using Microsoft.Extensions.Logging;

namespace CardScout.Core.Usage;

// Claude-backed features, as stored in CARD_AI_USAGE.FEATURE
public static class AiFeatures
{
    public const string Identify = "identify";            // front + back scan
    public const string IdentifyFront = "identify_front"; // show mode, front only
    public const string Lot = "lot";                      // every card in one photo
}

public class UsageOptions
{
    // Free users get a push once they pass this share of their monthly AI limit
    public int WarnAtPercent { get; set; } = 80;

    // Expected cost of one call, checked against the remaining budget before calling Claude.
    // Roughly a heavy call at the configured model and effort; tune from CARD_AI_USAGE.
    public Dictionary<string, decimal> CallEstimatesUsd { get; set; } = new()
    {
        [AiFeatures.Identify] = 0.08m,
        [AiFeatures.IdentifyFront] = 0.06m,
        [AiFeatures.Lot] = 0.15m
    };

    public decimal EstimateFor(string feature) => CallEstimatesUsd.TryGetValue(feature, out var v) ? v : 0.10m;
}

public record UsageStatus(
    string Plan,
    decimal UsedUsd,
    decimal LimitUsd,
    DateTime ResetsAtUtc,
    int ScansThisMonth)
{
    public decimal PercentUsed => LimitUsd <= 0 ? 100m : Math.Min(999m, Math.Round(UsedUsd / LimitUsd * 100m, 1));
}

public record UsageCheck(bool Allowed, UsageStatus Status);

// Meters Claude spend per user and enforces each plan's monthly AI limit.
// Call CheckAsync before a Claude call and RecordAsync after it (also when it failed but billed).
public class UsageMeter
{
    public const string WarnAlertKind = "WARN";

    private readonly AiUsageRepository _repo;
    private readonly ISubscriptionService _subscriptions;
    private readonly PlanCatalog _plans;
    private readonly AiCostCalculator _costs;
    private readonly ExpoPushClient _push;
    private readonly UsageOptions _options;
    private readonly ILogger<UsageMeter> _logger;

    public UsageMeter(AiUsageRepository repo, ISubscriptionService subscriptions, PlanCatalog plans, AiCostCalculator costs,
        ExpoPushClient push, UsageOptions options, ILogger<UsageMeter> logger)
    {
        _repo = repo;
        _subscriptions = subscriptions;
        _plans = plans;
        _costs = costs;
        _push = push;
        _options = options;
        _logger = logger;
    }

    public static DateTime MonthStartUtc(DateTime nowUtc) => new(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    public async Task<UsageStatus> GetStatusAsync(long userId)
    {
        var now = DateTime.UtcNow;
        var monthStart = MonthStartUtc(now);
        var plan = _plans.Get(await _subscriptions.GetPlanAsync(userId));
        var summary = await _repo.GetSummaryAsync(userId, monthStart);
        return new UsageStatus(plan.Id, Math.Round(summary.CostUsd, 4), plan.AiMonthlyLimitUsd, monthStart.AddMonths(1), summary.Calls);
    }

    // Allowed unless month-to-date spend plus this call's estimate would pass the plan's limit
    public async Task<UsageCheck> CheckAsync(long userId, string feature)
    {
        var status = await GetStatusAsync(userId);
        var allowed = status.UsedUsd + _options.EstimateFor(feature) <= status.LimitUsd;
        if (!allowed)
            _logger.LogInformation("AI limit reached for user {UserId} ({Plan}): {Used:F4} of {Limit} USD, {Feature} blocked",
                userId, status.Plan, status.UsedUsd, status.LimitUsd, feature);
        return new UsageCheck(allowed, status);
    }

    // Saves the call's cost. Never throws: a metering problem shouldn't fail a scan that already ran
    public async Task<decimal?> RecordAsync(long userId, string feature, ScanUsage? usage, bool succeeded)
    {
        if (usage == null) return null;
        try
        {
            if (!_costs.IsKnownModel(usage.Model))
                _logger.LogWarning("No price configured for model {Model}; charging the highest known rate. Add it under AnthropicPricing.", usage.Model);

            var cost = _costs.Cost(usage);
            var planId = _plans.Get(await _subscriptions.GetPlanAsync(userId)).Id;
            await _repo.InsertAsync(new AiUsageRecord(userId, DateTime.UtcNow, feature, usage.Model, usage.InputTokens,
                usage.OutputTokens, usage.CacheWriteTokens, usage.CacheReadTokens, cost, planId, succeeded));

            _logger.LogInformation("AI {Feature} by user {UserId}: {Model} {In} in / {Out} out / {CacheW} cache-write / {CacheR} cache-read tokens = ${Cost:F4}",
                feature, userId, usage.Model, usage.InputTokens, usage.OutputTokens, usage.CacheWriteTokens, usage.CacheReadTokens, cost);

            if (planId == Plans.Free)
                await WarnIfNearLimitAsync(userId);
            return cost;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record AI usage for user {UserId} ({Feature}, {Model})", userId, feature, usage.Model);
            return null;
        }
    }

    // One push per month when a free user passes WarnAtPercent of the limit
    private async Task WarnIfNearLimitAsync(long userId)
    {
        var status = await GetStatusAsync(userId);
        if (status.Plan != Plans.Free || status.PercentUsed < _options.WarnAtPercent) return;
        if (!await _repo.TryMarkAlertAsync(userId, MonthStartUtc(DateTime.UtcNow), WarnAlertKind)) return;

        var atLimit = status.PercentUsed >= 100;
        var message = new PushMessage(
            atLimit ? "You've used your free scans" : "Free scans running low",
            atLimit
                ? $"Your free AI scans reset on {status.ResetsAtUtc:MMM d}. Upgrade to Pro to keep scanning."
                : $"You've used {Math.Floor(status.PercentUsed)}% of this month's free AI scans. They reset on {status.ResetsAtUtc:MMM d}.",
            new Dictionary<string, string> { ["type"] = "usage" });
        try
        {
            await _push.SendAsync(userId, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Usage warning push failed for user {UserId}", userId);
        }
    }
}
