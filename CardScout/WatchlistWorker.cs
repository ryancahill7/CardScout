using CardScout.Core.Data;
using CardScout.Core.Ebay;
using CardScout.Core.Flip;
using CardScout.Core.Models;
using CardScout.Core.Notifications;
using CardScout.Core.Pricing;

namespace CardScout;

// Every 10 minutes, runs each user's eBay watchlists, prices listings it hasn't seen
// yet against SportsCardsPro, and pushes an alert for any that clear the watchlist's
// profit targets after fees and shipping. Every listing it prices is saved to
// WATCHLIST_HITS (profitable or not) so it's only priced once.
public class WatchlistWorker : BackgroundService
{
    // eBay's Browse API quota is per application (not per user), so keep sweeps modest and
    // share one search between every watchlist with the same query in a sweep
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    // Auctions further out than this will move too much to judge yet
    private static readonly TimeSpan AuctionWindow = TimeSpan.FromHours(24);
    // SportsCardsPro allows ~1 call/sec and a new title costs two calls, so cap each run
    private const int MaxNewListingsPerRun = 15;
    private const int SearchLimit = 30;

    private readonly ILogger<WatchlistWorker> _logger;
    private readonly CardUsersRepository _users;
    private readonly WatchlistRepository _watchlists;
    private readonly EbaySearchClient _ebay;
    private readonly ListingEvaluator _evaluator;
    private readonly ExpoPushClient _push;

    public WatchlistWorker(
        ILogger<WatchlistWorker> logger,
        CardUsersRepository users,
        WatchlistRepository watchlists,
        EbaySearchClient ebay,
        ListingEvaluator evaluator,
        ExpoPushClient push)
    {
        _logger = logger;
        _users = users;
        _watchlists = watchlists;
        _ebay = ebay;
        _evaluator = evaluator;
        _push = push;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var searches = new Dictionary<string, List<EbayListing>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var userId in await _users.GetActiveIdsAsync())
                {
                    foreach (var watchlist in await _watchlists.GetAllAsync(userId, activeOnly: true))
                    {
                        if (stoppingToken.IsCancellationRequested) break;
                        try
                        {
                            await RunAsync(watchlist, searches, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Watchlist {Id} ({Name}) failed", watchlist.Id, watchlist.Name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Watchlist sweep failed");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task RunAsync(Watchlist watchlist, Dictionary<string, List<EbayListing>> searches, CancellationToken ct)
    {
        var query = watchlist.Query.Trim();
        var listings = new List<EbayListing>();
        if (watchlist.ListingType != WatchlistListingType.Auction)
            listings.AddRange(await SearchOnceAsync(searches, $"BIN|{query}",
                () => _ebay.SearchListingsAsync(query, "FIXED_PRICE", "newlyListed", SearchLimit)));
        if (watchlist.ListingType != WatchlistListingType.FixedPrice)
            listings.AddRange(await SearchOnceAsync(searches, $"AUC|{query}",
                () => _ebay.SearchListingsAsync(query, "AUCTION", "endingSoonest", SearchLimit, endBefore: DateTime.UtcNow + AuctionWindow)));

        // Max price is applied here rather than in the eBay query, so lists with different caps share a search
        listings = listings
            .DistinctBy(l => l.ItemId)
            .Where(l => l.CurrentPrice > 0 && (watchlist.MaxPrice == null || l.CurrentPrice <= watchlist.MaxPrice))
            .ToList();
        var seen = await _watchlists.GetSeenItemIdsAsync(watchlist.UserId, watchlist.Id, listings.Select(l => l.ItemId).ToList());
        var fresh = listings.Where(l => !seen.Contains(l.ItemId)).Take(MaxNewListingsPerRun).ToList();

        var targets = new FlipTargets(watchlist.MinProfit, watchlist.MinProfitPct);
        var alerts = new List<WatchlistHit>();

        foreach (var listing in fresh)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var hit = await EvaluateAsync(watchlist, listing, targets);
                hit.Id = await _watchlists.InsertHitAsync(hit);
                if (hit.IsProfitable == "Y") alerts.Add(hit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Watchlist {Id}: failed to evaluate {ItemId}", watchlist.Id, listing.ItemId);
            }
        }

        await _watchlists.MarkRunAsync(watchlist.UserId, watchlist.Id);

        if (alerts.Count > 0)
        {
            _logger.LogInformation("Watchlist {Name}: {Count} profitable listing(s)", watchlist.Name, alerts.Count);
            if (watchlist.Notify == "Y") await NotifyAsync(watchlist, alerts, ct);
        }
    }

    private static async Task<List<EbayListing>> SearchOnceAsync(
        Dictionary<string, List<EbayListing>> searches, string key, Func<Task<List<EbayListing>>> search)
    {
        if (!searches.TryGetValue(key, out var results))
        {
            results = await search();
            searches[key] = results;
        }
        return results;
    }

    private async Task<WatchlistHit> EvaluateAsync(Watchlist watchlist, EbayListing listing, FlipTargets targets)
    {
        var hit = new WatchlistHit
        {
            WatchlistId = watchlist.Id,
            UserId = watchlist.UserId,
            ItemId = listing.ItemId,
            Title = listing.Title,
            ListingType = listing.ListingType,
            Price = listing.CurrentPrice,
            ShippingCost = listing.ShippingCost,
            ShippingEstimated = listing.ShippingCost == null ? "Y" : "N",
            EndTime = listing.EndTime,
            ListingUrl = listing.ItemUrl,
            ImageUrl = listing.ImageUrl
        };

        var result = await _evaluator.MatchAsync(listing.Title);
        if (result.Match == null) return hit;

        var (comp, confidence, parsed) = result.Match;
        hit.CompProductId = comp.ProductId;
        hit.CompProductName = comp.ProductName;
        hit.CompSetName = comp.ConsoleName;
        hit.MatchConfidence = (decimal)Math.Round(confidence, 3);
        hit.CompTier = CompPriceSelector.SelectTier(parsed.Grade);

        if (watchlist.GradedOnly == "Y" && parsed.Grade == null) return hit;

        var market = CompPriceSelector.SelectPrice(comp, parsed.Grade);
        hit.CompPrice = market;
        if (market is null or 0) return hit;

        var shipping = listing.ShippingCost ?? SellerFees.EstimateBuyShipping(listing.CurrentPrice);
        var flip = FlipAnalyzer.Analyze(market, listing.CurrentPrice, shipping, targets, confidence);

        // Real shipping costs an extra eBay call, so only fetch it when the estimate already looks good
        if (flip.MeetsTargets && listing.ShippingCost == null)
        {
            var actual = await _ebay.GetActualShippingCostAsync(listing.ItemId);
            if (actual != null)
            {
                hit.ShippingCost = actual;
                hit.ShippingEstimated = "N";
                flip = FlipAnalyzer.Analyze(market, listing.CurrentPrice, actual.Value, targets, confidence);
            }
        }
        if (hit.ShippingEstimated == "Y") hit.ShippingCost = shipping;

        hit.NetProfit = flip.NetProfit;
        hit.NetProfitPct = flip.NetProfitPct;
        hit.MaxBuyPrice = flip.MaxBuyPrice;
        hit.IsProfitable = flip.MeetsTargets ? "Y" : "N";
        return hit;
    }

    private async Task NotifyAsync(Watchlist watchlist, List<WatchlistHit> alerts, CancellationToken ct)
    {
        PushMessage message;
        if (alerts.Count == 1)
        {
            var hit = alerts[0];
            var detail = hit.ListingType == "AUCTION"
                ? $"Bid ${hit.Price:0.00}, worth bidding up to ${hit.MaxBuyPrice:0.00}"
                : $"Buy It Now ${hit.Price:0.00}";
            message = new PushMessage(
                $"+${hit.NetProfit:0.00} flip · {watchlist.Name}",
                $"{hit.Title}\n{detail} · comp ${hit.CompPrice:0.00} · {hit.NetProfitPct:0}% ROI",
                new Dictionary<string, string> { ["type"] = "watchlist-hit", ["hitId"] = hit.Id.ToString() });
        }
        else
        {
            var best = alerts.MaxBy(h => h.NetProfit ?? 0)!;
            message = new PushMessage(
                $"{alerts.Count} profitable listings · {watchlist.Name}",
                $"Best: +${best.NetProfit:0.00} on {best.Title}",
                new Dictionary<string, string> { ["type"] = "watchlist", ["watchlistId"] = watchlist.Id.ToString() });
        }

        var sent = await _push.SendAsync(watchlist.UserId, message, ct);
        if (sent > 0) await _watchlists.MarkNotifiedAsync(watchlist.UserId, alerts.Select(a => a.Id).ToList());
    }
}
