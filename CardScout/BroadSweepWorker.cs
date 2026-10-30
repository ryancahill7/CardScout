using CardScout.Core.Data;
using CardScout.Core.Ebay;
using CardScout.Core.Models;

namespace CardScout;

public class BroadSweepWorker : BackgroundService
{
    private readonly ILogger<BroadSweepWorker> _logger;
    private readonly EbaySearchClient _ebay;
    private readonly EbayListingsRepository _listingsRepo;

    public BroadSweepWorker(ILogger<BroadSweepWorker> logger, EbaySearchClient ebay, EbayListingsRepository listingsRepo)
    {
        _logger = logger;
        _ebay = ebay;
        _listingsRepo = listingsRepo;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var listings = await _ebay.GetBroadSweepAsync(50);

                int newCount = 0;
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
                        ShippingCost = listing.ShippingCost
                    });
                    if (wasNew) newCount++;
                }

                if (newCount > 0)
                    _logger.LogInformation("Broad sweep: {New} new listings inserted", newCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Broad sweep poll failed");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}