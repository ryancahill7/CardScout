using CardScout.Core.Data;
using CardScout.Core.Models;
using CardScout.Core.Notifications;
using CardScout.Core.Pricing;

namespace CardScout;

// Re-prices the cards users still hold (once a day per card), records a snapshot in
// CARD_PRICE_HISTORY, and pushes an alert when a card's market price has moved a lot over
// the past week ("your PSA 9 is up 22%, good time to sell").
public class PortfolioPriceWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan RepriceAfter = TimeSpan.FromHours(23);
    private static readonly TimeSpan MoveWindow = TimeSpan.FromDays(7);
    private const decimal MoveAlertPct = 15m;
    private const decimal MoveAlertDollars = 3m;
    private const int MaxCardsPerUserPerRun = 40;

    private readonly ILogger<PortfolioPriceWorker> _logger;
    private readonly CardUsersRepository _users;
    private readonly CardInventoryRepository _inventory;
    private readonly PriceHistoryRepository _history;
    private readonly CardPricingService _pricing;
    private readonly ExpoPushClient _push;

    public PortfolioPriceWorker(
        ILogger<PortfolioPriceWorker> logger,
        CardUsersRepository users,
        CardInventoryRepository inventory,
        PriceHistoryRepository history,
        CardPricingService pricing,
        ExpoPushClient push)
    {
        _logger = logger;
        _users = users;
        _inventory = inventory;
        _history = history;
        _pricing = pricing;
        _push = push;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var userId in await _users.GetActiveIdsAsync())
                    await RunForUserAsync(userId, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Portfolio price sweep failed");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task RunForUserAsync(long userId, CancellationToken ct)
    {
        var cards = await _inventory.GetHeldForRepriceAsync(userId, RepriceAfter, MaxCardsPerUserPerRun);
        var moves = new List<(CardInventoryItem Card, decimal From, decimal To)>();

        foreach (var card in cards)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                var quote = await _pricing.QuoteAsync(card.ToIdentification(), card.CompProductId);
                var price = quote.SuggestedPrice;
                await _inventory.UpdateMarketPriceAsync(userId, card.Id, price);
                if (price == null) continue;

                var history = await _history.GetForCardAsync(userId, card.Id, days: 30);
                await _history.InsertAsync(userId, card.Id, price.Value);

                // Compare with the newest snapshot that's at least a week old (or the oldest we have)
                var baseline = history.LastOrDefault(p => p.CapturedAt <= DateTime.Now - MoveWindow) ?? history.FirstOrDefault();
                var previous = history.LastOrDefault();
                // Only alert when the price itself changed this time, so a big move alerts once
                if (baseline == null || previous == null || previous.Price == price.Value) continue;

                var change = price.Value - baseline.Price;
                var pct = baseline.Price == 0 ? 0 : change / baseline.Price * 100m;
                if (Math.Abs(pct) >= MoveAlertPct && Math.Abs(change) >= MoveAlertDollars)
                    moves.Add((card, baseline.Price, price.Value));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Couldn't re-price card {CardId} for user {UserId}", card.Id, userId);
            }
        }

        foreach (var (card, from, to) in moves.OrderByDescending(m => Math.Abs(m.To - m.From)).Take(3))
        {
            var up = to > from;
            var pct = (to - from) / from * 100m;
            await _push.SendAsync(userId, new PushMessage(
                $"{(up ? "📈" : "📉")} {card.Player ?? "Your card"} {(up ? "up" : "down")} {Math.Abs(pct):0}%",
                $"{card.EbayTitle}\n${from:0.00} → ${to:0.00}{(up ? " · could be a good time to sell" : "")}",
                new Dictionary<string, string> { ["type"] = "card", ["cardId"] = card.Id.ToString() }), ct);
        }
    }
}
