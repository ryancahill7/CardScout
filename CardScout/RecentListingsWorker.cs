using CardScout.Core.Data;
using CardScout.Core.Ebay;
using CardScout.Core.Models;

namespace CardScout;

public class RecentListingsWorker : BackgroundService
{
    private readonly ILogger<RecentListingsWorker> _logger;
    private readonly EbaySearchClient _ebay;
    private readonly RecentListingsRepository _repo;

    private static readonly string[] TrackedPlayers = { "Mason Miller", "Craig Biggio" };

    public RecentListingsWorker(ILogger<RecentListingsWorker> logger, EbaySearchClient ebay, RecentListingsRepository repo)
    {
        _logger = logger;
        _ebay = ebay;
        _repo = repo;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var player in TrackedPlayers)
            {
                try
                {
                    var listings = await _ebay.GetNewestListingsAsync(player, 20);

                    int newCount = 0;
                    foreach (var listing in listings)
                    {
                        var wasNew = await _repo.InsertIfNewAsync(new RecentListing
                        {
                            ItemId = listing.ItemId,
                            PlayerName = player,
                            Title = listing.Title,
                            Price = listing.CurrentPrice,
                            ListingType = listing.ListingType,
                            Condition = listing.Condition,
                            ListingUrl = listing.ItemUrl,
                            FirstSeen = DateTime.UtcNow
                        });
                        if (wasNew) newCount++;
                    }

                    if (newCount > 0)
                        _logger.LogInformation("{Player} recent listings: {New} new", player, newCount);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed polling recent listings for {Player}", player);
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        }
    }
}