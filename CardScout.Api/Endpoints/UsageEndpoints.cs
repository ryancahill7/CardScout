using CardScout.Core.Billing;
using CardScout.Core.Usage;

namespace CardScout.Api.Endpoints;

public static class UsageEndpoints
{
    public const string UsageLimitCode = "USAGE_LIMIT";
    public const string PlanLimitCode = "PLAN_LIMIT";

    public static void MapUsageEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/usage", GetUsageAsync);
        app.MapGet("/api/plans", (PlanCatalog plans) => Results.Ok(plans.All.Select(PlanDto.From)));
    }

    private static async Task<IResult> GetUsageAsync(CurrentUser user, UsageMeter meter) =>
        Results.Ok(UsageDto.From(await meter.GetStatusAsync(user.UserId)));

    // HTTP 402 when a Claude call would pass the user's monthly AI limit. The app opens the paywall on this.
    public static IResult UsageLimitReached(UsageStatus s) => Results.Json(
        new
        {
            code = UsageLimitCode,
            error = s.Plan == Plans.Free
                ? "You've used this month's free AI scans. Upgrade to Pro to keep scanning."
                : "You've hit this month's fair-use limit for AI scans. Contact support if you need more.",
            plan = s.Plan,
            usedUsd = Math.Round(s.UsedUsd, 2),
            limitUsd = s.LimitUsd,
            resetsAt = s.ResetsAtUtc
        },
        statusCode: StatusCodes.Status402PaymentRequired);

    // HTTP 402 when a non-AI plan limit (e.g. number of watchlists) is reached
    public static IResult PlanLimitReached(string plan, string feature, int limit, string message) => Results.Json(
        new { code = PlanLimitCode, error = message, plan, feature, limit },
        statusCode: StatusCodes.Status402PaymentRequired);
}

public record UsageDto(string Plan, decimal UsedUsd, decimal LimitUsd, decimal PercentUsed, DateTime ResetsAt, int ScansThisMonth)
{
    public static UsageDto From(UsageStatus s) =>
        new(s.Plan, Math.Round(s.UsedUsd, 2), s.LimitUsd, s.PercentUsed, s.ResetsAtUtc, s.ScansThisMonth);
}

public record PlanDto(string Id, string Name, decimal MonthlyPriceUsd, decimal YearlyPriceUsd, decimal AiMonthlyLimitUsd,
    int MaxWatchlists, IReadOnlyList<string> Features)
{
    public static PlanDto From(PlanDefinition p) =>
        new(p.Id, p.Name, p.MonthlyPriceUsd, p.YearlyPriceUsd, p.AiMonthlyLimitUsd, p.MaxWatchlists, p.Features);
}
