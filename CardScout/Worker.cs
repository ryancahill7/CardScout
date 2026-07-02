using CardScout.Core.Data;
using CardScout.Core.Ebay;
using CardScout.Core.Models;

namespace CardScout;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly EbaySearchClient _ebay;
    private readonly EbayListingsRepository _listingsRepo;
    private readonly WatchedPlayersRepository _watchedPlayers;

    public Worker(
        ILogger<Worker> logger,
        EbaySearchClient ebay,
        EbayListingsRepository listingsRepo,
        WatchedPlayersRepository watchedPlayers)
    {
        _logger = logger;
        _ebay = ebay;
        _listingsRepo = listingsRepo;
        _watchedPlayers = watchedPlayers;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var players = await _watchedPlayers.GetActivePlayerNamesAsync();

            foreach (var player in players)
            {
                await PollAndStoreAsync(player, isAuction: true);
                await PollAndStoreAsync(player, isAuction: false);
            }

            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        }
    }

    private async Task PollAndStoreAsync(string player, bool isAuction)
    {
        try
        {
            var listings = isAuction
                ? await _ebay.GetAuctionListingsForPlayerAsync(player, 20)
                : await _ebay.GetFixedPriceListingsForPlayerAsync(player, 20);

            int newCount = 0;
            foreach (var listing in listings)
            {
                if (isAuction && listing.EndTime.HasValue && listing.EndTime.Value > DateTime.UtcNow.AddHours(1))
                    continue;

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
                    ShippingCost = listing.ShippingCost
                });
                if (wasNew) newCount++;
            }

            if (newCount > 0)
                _logger.LogInformation("{Player} ({Type}): {New} new listings inserted",
                    player, isAuction ? "AUCTION" : "FIXED_PRICE", newCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed polling {Player} ({Type})", player, isAuction ? "AUCTION" : "FIXED_PRICE");
        }
    }
}