using CardScout.Core.Data;
using CardScout.Core.Ebay;

namespace CardScout;

public class AvailabilityCheckWorker : BackgroundService
{
    private readonly ILogger<AvailabilityCheckWorker> _logger;
    private readonly EbayListingsRepository _listingsRepo;
    private readonly EbaySearchClient _ebay;

    public AvailabilityCheckWorker(ILogger<AvailabilityCheckWorker> logger, EbayListingsRepository listingsRepo, EbaySearchClient ebay)
    {
        _logger = logger;
        _listingsRepo = listingsRepo;
        _ebay = ebay;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            List<string> itemIds;
            try
            {
                itemIds = await _listingsRepo.GetActiveDealItemIdsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Couldn't load active deals; retrying in 15 minutes");
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
                continue;
            }
            int soldCount = 0;

            foreach (var itemId in itemIds)
            {
                try
                {
                    var stillActive = await _ebay.IsListingStillActiveAsync(itemId);
                    if (!stillActive)
                    {
                        await _listingsRepo.MarkSoldAsync(itemId);
                        soldCount++;
                    }
                    await Task.Delay(200, stoppingToken); // gentle pacing, avoid bursting eBay
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Availability check failed for {ItemId}", itemId);
                }
            }

            if (soldCount > 0)
                _logger.LogInformation("Marked {Count} listings as sold/removed", soldCount);

            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }
}