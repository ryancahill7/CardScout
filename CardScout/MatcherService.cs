using CardScout.Core.Data;
using CardScout.Core.Ebay;
using CardScout.Core.Flip;
using CardScout.Core.Models;
using CardScout.Core.Pricing;

namespace CardScout;

public class MatcherService : BackgroundService
{
    private readonly ILogger<MatcherService> _logger;
    private readonly EbayListingsRepository _listingsRepo;
    private readonly DealsRepository _dealsRepo;
    private readonly ListingEvaluator _evaluator;
    private readonly EbaySearchClient _ebay;

    private const decimal MinNetProfitDollars = 2m;
    private const decimal MinNetProfitPct = 5m;
    public MatcherService(
        ILogger<MatcherService> logger,
        EbayListingsRepository listingsRepo,
        DealsRepository dealsRepo,
        ListingEvaluator evaluator,
        EbaySearchClient ebay)
    {
        _logger = logger;
        _listingsRepo = listingsRepo;
        _dealsRepo = dealsRepo;
        _evaluator = evaluator;
        _ebay = ebay;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var pending = await _listingsRepo.GetPendingAsync(50);

            foreach (var listing in pending)
            {
                try
                {
                    await ProcessListingAsync(listing);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process listing {ItemId}", listing.ItemId);
                    await _listingsRepo.UpdateMatchStatusAsync(listing.ItemId, "ERROR"); // add this
                }
            }

            if (pending.Count == 0)
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task ProcessListingAsync(EbayListingRecord listing)
    {
        var result = await _evaluator.MatchAsync(listing.Title);
        if (result.Match == null)
        {
            await _listingsRepo.UpdateMatchStatusAsync(listing.ItemId, "NO_MATCH");
            _logger.LogDebug("NO_MATCH ({Reason}): {Title}", result.NoMatchReason, listing.Title);
            return;
        }

        var (compRecord, confidence, parsed) = result.Match;

        var compPrice = CompPriceSelector.SelectPrice(compRecord, parsed.Grade);

        if (compPrice is null or 0)
        {
            await _listingsRepo.UpdateMatchStatusAsync(listing.ItemId, "NO_MATCH");
            _logger.LogDebug("NO_MATCH (no comp price for grade tier): {Title}", listing.Title);
            return;
        }

        var discountPct = (compPrice.Value - listing.CurrentPrice) / compPrice.Value * 100m;
        discountPct = Math.Clamp(discountPct, -99999.99m, 99999.99m);

        var buyShipping = listing.ShippingCost ?? SellerFees.EstimateBuyShipping(listing.CurrentPrice);
        var acquisitionCost = listing.CurrentPrice + buyShipping;

        var netProceeds = SellerFees.NetProceeds(compPrice.Value);

        var netProfit = netProceeds - acquisitionCost;
        var netProfitPct = acquisitionCost == 0 ? 0 : netProfit / acquisitionCost * 100m;
        netProfitPct = Math.Clamp(netProfitPct, -99999.99m, 99999.99m);

        var isProfitable = netProfit >= MinNetProfitDollars && netProfitPct >= MinNetProfitPct;

        // Only spend the extra eBay call refining shipping on listings that ALREADY look
        // profitable on the estimate — this is the expensive, rate-limit-sensitive step,
        // so it should stay reserved for genuinely promising matches, not every match.
        if (isProfitable && !listing.ShippingCost.HasValue)
        {
            var realShipping = await _ebay.GetActualShippingCostAsync(listing.ItemId);
            if (realShipping.HasValue)
            {
                await _listingsRepo.UpdateShippingCostAsync(listing.ItemId, realShipping.Value);
                buyShipping = realShipping.Value;
                acquisitionCost = listing.CurrentPrice + buyShipping;
                netProfit = netProceeds - acquisitionCost;
                netProfitPct = acquisitionCost == 0 ? 0 : netProfit / acquisitionCost * 100m;
                netProfitPct = Math.Clamp(netProfitPct, -99999.99m, 99999.99m);
                isProfitable = netProfit >= MinNetProfitDollars && netProfitPct >= MinNetProfitPct;
            }
        }

        await _dealsRepo.InsertDealAsync(new DealRecord
        {
            ItemId = listing.ItemId,
            CompProductId = compRecord.ProductId ?? "",
            CompPriceUsed = compPrice.Value,
            DiscountPct = discountPct,
            MatchConfidence = (decimal)confidence,
            NetProfit = netProfit,
            NetProfitPct = netProfitPct,
            IsProfitable = isProfitable ? "Y" : "N",
            CreatedAt = DateTime.UtcNow
        });

        if (isProfitable)
        {
            _logger.LogInformation(
                "DEAL FOUND ({Confidence:F2} confidence): {Title} — ${NetProfit:F2} net profit ({NetProfitPct:F1}%) — matched to {CompName}",
                confidence, listing.Title, netProfit, netProfitPct, compRecord.ProductName);
        }
        else
        {
            _logger.LogDebug("MATCHED but unprofitable (${NetProfit:F2}, {NetProfitPct:F1}%): {Title}", netProfit, netProfitPct, listing.Title);
        }

        await _listingsRepo.UpdateMatchStatusAsync(listing.ItemId, "MATCHED");
    }
}