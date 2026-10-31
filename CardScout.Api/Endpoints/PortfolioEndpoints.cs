using System.Globalization;
using System.Text;
using CardScout.Core.Data;
using CardScout.Core.Models;
using CardScout.Core.Notifications;
using CardScout.Core.Pricing;

namespace CardScout.Api.Endpoints;

// P&L, market deals, and push-token registration
public static class PortfolioEndpoints
{
    private static readonly TimeZoneInfo Central = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    public static void MapPortfolioEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/portfolio", SummaryAsync);
        app.MapGet("/api/portfolio/export", ExportAsync);
        app.MapGet("/api/deals", DealsAsync);
        app.MapPost("/api/push-tokens", RegisterPushAsync);
        app.MapDelete("/api/push-tokens", UnregisterPushAsync);
        app.MapPost("/api/push-tokens/test", TestPushAsync);
    }

    // Sales are assumed to be on eBay, so fees use the same SellerFees math as everywhere else
    private static decimal TotalCost(CardInventoryItem c) => (c.CostBasis ?? 0) + (c.GradingCost ?? 0);
    private static decimal? Value(CardInventoryItem c) => c.MarketPrice ?? c.SuggestedPrice;
    private static decimal SaleProfit(CardInventoryItem c) =>
        SellerFees.NetProceeds(c.SoldPrice!.Value) - (c.SaleShippingCost ?? 0) - TotalCost(c);

    private static async Task<IResult> SummaryAsync(CurrentUser user, CardInventoryRepository repo, PriceHistoryRepository history)
    {
        var cards = await repo.GetAllAsync(user.UserId, limit: 5000);
        var held = cards.Where(c => c.Status != CardStatus.Sold).ToList();
        var priced = held.Where(c => Value(c) is > 0).ToList();
        var sold = cards.Where(c => c.Status == CardStatus.Sold && c.SoldPrice != null).ToList();

        var heldSummary = new PortfolioHeld(
            held.Count,
            priced.Count,
            priced.Sum(c => Value(c)!.Value),
            held.Sum(TotalCost),
            Math.Round(priced.Sum(c => SellerFees.NetProceeds(Value(c)!.Value) - TotalCost(c)), 2));

        var soldSummary = new PortfolioSold(
            sold.Count,
            sold.Sum(c => c.SoldPrice!.Value),
            Math.Round(sold.Sum(c => c.SoldPrice!.Value - SellerFees.NetProceeds(c.SoldPrice!.Value)), 2),
            sold.Sum(c => c.SaleShippingCost ?? 0),
            sold.Sum(TotalCost),
            Math.Round(sold.Sum(SaleProfit), 2));

        // Last 12 months: what sold, what it made, and what was spent buying cards
        var months = Enumerable.Range(0, 12).Select(i => DateTime.Today.AddMonths(-i)).Select(d => d.ToString("yyyy-MM")).Reverse();
        var monthly = months.Select(m =>
        {
            var soldIn = sold.Where(c => c.SoldAt?.ToString("yyyy-MM") == m).ToList();
            var bought = cards.Where(c => (c.PurchasedAt ?? c.CreatedAt).ToString("yyyy-MM") == m);
            return new PortfolioMonth(m, soldIn.Count, soldIn.Sum(c => c.SoldPrice!.Value),
                Math.Round(soldIn.Sum(SaleProfit), 2), bought.Sum(TotalCost));
        }).ToList();

        // Biggest market moves over the last ~week among held cards
        var points = await history.GetForUserAsync(user.UserId, days: 30);
        var byId = held.ToDictionary(c => c.Id);
        var weekAgo = DateTime.Now.AddDays(-7);
        var movers = points
            .GroupBy(p => p.InventoryId)
            .Where(g => byId.ContainsKey(g.Key) && g.Count() >= 2)
            .Select(g =>
            {
                var latest = g.Last();
                var baseline = g.LastOrDefault(p => p.CapturedAt <= weekAgo) ?? g.First();
                var card = byId[g.Key];
                var pct = baseline.Price == 0 ? 0 : Math.Round((latest.Price - baseline.Price) / baseline.Price * 100m, 1);
                return new PortfolioMover(card.Id, card.EbayTitle, card.Player, baseline.Price, latest.Price, pct,
                    card.FrontImagePath == null ? null : $"/api/cards/{card.Id}/images/front");
            })
            .Where(m => m.From != m.To)
            .OrderByDescending(m => Math.Abs(m.ChangePct))
            .Take(8)
            .ToList();

        return Results.Ok(new PortfolioResponse(heldSummary, soldSummary, monthly, movers));
    }

    // CSV of every card with its costs and sale, for taxes (Schedule C) or a spreadsheet
    private static async Task<IResult> ExportAsync(CurrentUser user, CardInventoryRepository repo)
    {
        var cards = await repo.GetAllAsync(user.UserId, limit: 5000);
        var sb = new StringBuilder();
        sb.AppendLine("Id,Title,Player,Year,Set,Card Number,Grade,Status,Purchase Source,Purchased,Cost,Grading Cost," +
                      "Market Value,Listed Price,Sold Price,Sold,eBay Fees,Sale Shipping,Net Profit");
        foreach (var c in cards.OrderBy(c => c.CreatedAt))
        {
            decimal? fees = c.SoldPrice == null ? null : c.SoldPrice.Value - SellerFees.NetProceeds(c.SoldPrice.Value);
            decimal? profit = c.SoldPrice == null ? null : SaleProfit(c);
            var row = new object?[]
            {
                c.Id, c.EbayTitle, c.Player, c.CardYear, string.Join(" ", new[] { c.Manufacturer, c.SetName }.Where(s => s != null)),
                c.CardNumber, c.GradingCompany == null ? "Raw" : $"{c.GradingCompany} {c.Grade:0.#}", c.Status,
                c.PurchaseSource, (c.PurchasedAt ?? c.CreatedAt).ToString("yyyy-MM-dd"), c.CostBasis, c.GradingCost,
                Value(c), c.ListedPrice, c.SoldPrice, c.SoldAt?.ToString("yyyy-MM-dd"), fees, c.SaleShippingCost, profit
            };
            sb.AppendLine(string.Join(",", row.Select(Csv)));
        }

        return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv",
            $"cardscout-{DateTime.Today:yyyy-MM-dd}.csv");
    }

    private static string Csv(object? value)
    {
        var s = value switch
        {
            null => "",
            decimal d => d.ToString("0.00", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
        };
        return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }

    // Profitable listings found by the shared MatcherService (WATCHED_PLAYERS), not per user
    private static async Task<IResult> DealsAsync(DealsRepository deals)
    {
        var rows = await deals.GetDealsDetailAsync(onlyProfitable: true);
        return Results.Ok(rows.Take(150).Select(d => new MarketDealDto(
            d.ItemId, d.EbayTitle, d.ListingType, d.EbayPrice, d.ShippingCost, d.EbayCondition,
            d.AuctionEndTimeLocal == null
                ? null
                : TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(d.AuctionEndTimeLocal.Value, DateTimeKind.Unspecified), Central),
            d.EbayUrl, d.CompCardName, d.CompSetName, d.CompUrl, d.CompPrice, d.DiscountPct, d.NetProfit, d.NetProfitPct,
            d.MatchConfidence, d.DealFoundAt)));
    }

    private static async Task<IResult> RegisterPushAsync(PushTokenRequest request, CurrentUser user, PushTokenRepository tokens)
    {
        var token = request.Token?.Trim() ?? "";
        if (!token.StartsWith("ExponentPushToken[", StringComparison.Ordinal) && !token.StartsWith("ExpoPushToken[", StringComparison.Ordinal))
            return Results.BadRequest(new { error = "token must be an Expo push token." });
        await tokens.UpsertAsync(user.UserId, token, request.Platform);
        return Results.NoContent();
    }

    private static async Task<IResult> UnregisterPushAsync(string token, CurrentUser user, PushTokenRepository tokens)
    {
        await tokens.DeleteAsync(user.UserId, token);
        return Results.NoContent();
    }

    private static async Task<IResult> TestPushAsync(CurrentUser user, ExpoPushClient push, CancellationToken ct)
    {
        var sent = await push.SendAsync(user.UserId,
            new PushMessage("CardScout alerts are on", "You'll get a push like this when a watchlist finds a profitable flip."), ct);
        return Results.Ok(new { sent });
    }
}
