using CardScout.Core.Data;
using CardScout.Core.Ebay;
using CardScout.Core.Models;

namespace CardScout;

// The trading card game counterpart of BroadSweepWorker: once an hour, saves the newest listings for
// each game to EBAY_LISTINGS (CATEGORY = 'tcg', GAME set), where MatcherService prices them against
// PriceCharting and writes DEALS. 5 eBay calls an hour.
public class TcgSweepWorker : BackgroundService
{
    private static readonly (string Game, string Query)[] Games =
    [
        ("pokemon", "pokemon card"),
        ("mtg", "magic the gathering card"),
        ("yugioh", "yugioh card"),
        ("one-piece", "one piece card"),
        ("lorcana", "lorcana card"),
    ];

    private const int PerGame = 30;

    private readonly ILogger<TcgSweepWorker> _logger;
    private readonly EbaySearchClient _ebay;
    private readonly EbayListingsRepository _listingsRepo;

    public TcgSweepWorker(ILogger<TcgSweepWorker> logger, EbaySearchClient ebay, EbayListingsRepository listingsRepo)
    {
        _logger = logger;
        _ebay = ebay;
        _listingsRepo = listingsRepo;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var (game, query) in Games)
            {
                try
                {
                    var listings = await _ebay.SearchListingsAsync(query, buyingOption: null, sort: "newlyListed",
                        limit: PerGame, categoryId: EbaySearchClient.TcgCategoryId);

                    var newCount = 0;
                    foreach (var listing in listings)
                    {
                        var wasNew = await _listingsRepo.InsertIfNewAsync(new EbayListingRecord
                        {
                            ItemId = listing.ItemId,
                            Title = listing.Title,
                            CurrentPrice = listing.CurrentPrice,
                            Condition = listing.Condition,
                            EndTime = listing.EndTime,
                            ListingUrl = listing.ItemUrl,
                            FirstSeen = DateTime.UtcNow,
                            ListingType = listing.ListingType,
                            ShippingCost = listing.ShippingCost,
                            Category = "tcg",
                            Game = game
                        });
                        if (wasNew) newCount++;
                    }

                    if (newCount > 0)
                        _logger.LogInformation("TCG sweep ({Game}): {New} new listings inserted", game, newCount);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "TCG sweep failed for {Game}", game);
                }
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
