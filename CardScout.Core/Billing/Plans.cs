using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace CardScout.Core.Billing;

// What each plan costs and includes. This file is the single definition: the API enforces
// the limits from it and serves it to the app (GET /api/plans) for the paywall, so prices and
// features are never duplicated in the mobile code. Numeric limits and prices can be
// overridden in config under Billing:Plans:{free|pro}:* without a code change.
public record PlanDefinition(
    string Id,
    string Name,
    decimal MonthlyPriceUsd,
    decimal YearlyPriceUsd,
    // Claude spend allowed per calendar month (UTC). For Pro this is an abuse guard, not a quota
    decimal AiMonthlyLimitUsd,
    // Active eBay watchlists (eBay's Browse quota is per app, shared by every user)
    int MaxWatchlists,
    IReadOnlyList<string> Features);

public static class Plans
{
    public const string Free = "free";
    public const string Pro = "pro";

    // Pro is priced between the scanner apps (CollX Pro, Ludex Standard: ~$9.99/mo) and Card Ladder Pro
    // ($19.99/mo). An Opus scan costs ~$0.06-0.08, so a typical Pro user (~60-100 scans) costs $4-8/mo
    // against ~$14.25 net of Stripe fees. The Pro AI guard ($20) caps the worst case near break-even.
    // Yearly is 33% off (4 months free) to pull people past the post-show churn.
    public static readonly PlanDefinition FreeDefault = new(
        Free, "Free", 0m, 0m,
        AiMonthlyLimitUsd: 5.00m,
        MaxWatchlists: 3,
        Features:
        [
            "AI card and lot scans up to a monthly limit (about 60 cards)",
            "Live SportsCardsPro prices and full grade ladder",
            "Show mode flip verdicts",
            "Deals feed and active eBay listings",
            "3 eBay watchlists with alerts",
            "Collection and P&L tracking"
        ]);

    public static readonly PlanDefinition ProDefault = new(
        Pro, "Pro", 14.99m, 119.99m,
        AiMonthlyLimitUsd: 20.00m,
        MaxWatchlists: 25,
        Features:
        [
            "Unlimited AI card scans (fair use)",
            "Unlimited lot and binder-page analysis",
            "25 eBay watchlists with instant alerts",
            "Portfolio price alerts",
            "Everything in Free"
        ]);
}

public class PlanCatalog
{
    private readonly Dictionary<string, PlanDefinition> _plans;

    public PlanCatalog(IEnumerable<PlanDefinition> plans)
    {
        _plans = plans.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<PlanDefinition> All => _plans.Values;

    // Unknown plan ids fall back to Free, so a bad subscription row can't lift the cap
    public PlanDefinition Get(string? planId) =>
        planId != null && _plans.TryGetValue(planId, out var plan) ? plan : _plans[Plans.Free];

    // Reads overrides from Billing:Plans:{id}:{AiMonthlyLimitUsd|MaxWatchlists|MonthlyPriceUsd|YearlyPriceUsd}
    public static PlanCatalog FromConfiguration(IConfiguration config)
    {
        PlanDefinition Apply(PlanDefinition p)
        {
            var section = config.GetSection($"Billing:Plans:{p.Id}");
            return p with
            {
                AiMonthlyLimitUsd = Decimal(section["AiMonthlyLimitUsd"]) ?? p.AiMonthlyLimitUsd,
                MonthlyPriceUsd = Decimal(section["MonthlyPriceUsd"]) ?? p.MonthlyPriceUsd,
                YearlyPriceUsd = Decimal(section["YearlyPriceUsd"]) ?? p.YearlyPriceUsd,
                MaxWatchlists = int.TryParse(section["MaxWatchlists"], out var w) ? w : p.MaxWatchlists
            };
        }

        return new PlanCatalog([Apply(Plans.FreeDefault), Apply(Plans.ProDefault)]);
    }

    private static decimal? Decimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
}
