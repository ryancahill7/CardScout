using CardScout.Core.Comps;
using CardScout.Core.Data;
using CardScout.Core.Ebay;
using CardScout.Core.Matching;
using CardScout.Core.Models;
using CardScout.Core.Pricing;

namespace CardScout;

public class MatcherService : BackgroundService
{
    private readonly ILogger<MatcherService> _logger;
    private readonly EbayListingsRepository _listingsRepo;
    private readonly CompCacheRepository _compCacheRepo;
    private readonly DealsRepository _dealsRepo;
    private readonly CompsClient _compsClient;
    private readonly EbaySearchClient _ebay;

    private const double MinConfidence = 0.55;
    private const decimal MinNetProfitDollars = 2m;
    private const decimal MinNetProfitPct = 5m;
    private static decimal EstimateBuyShipping(decimal price)
    {
        return price switch
        {
            < 10m => 1.30m,   // plain tracked envelope (USPS Ground Advantage letter-ish rate)
            < 30m => 3.50m,   // small bubble mailer
            < 100m => 5.00m,  // padded mailer, toploader, team bag
            _ => 7.50m         // higher-value item, more careful packaging
        };
    }
    public MatcherService(
        ILogger<MatcherService> logger,
        EbayListingsRepository listingsRepo,
        CompCacheRepository compCacheRepo,
        DealsRepository dealsRepo,
        CompsClient compsClient,
        EbaySearchClient ebay)
    {
        _logger = logger;
        _listingsRepo = listingsRepo;
        _compCacheRepo = compCacheRepo;
        _dealsRepo = dealsRepo;
        _compsClient = compsClient;
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
        var parsed = TitleCleaner.Parse(listing.Title);

        var compRecord = await _compCacheRepo.GetCachedAsync(parsed.CleanedForSearch);
        double confidence;

        if (compRecord == null)
        {
            var candidates = await _compsClient.SearchAsync(parsed.CleanedForSearch);
            if (candidates.Count == 0)
            {
                await _listingsRepo.UpdateMatchStatusAsync(listing.ItemId, "NO_MATCH");
                _logger.LogDebug("NO_MATCH (no candidates): {Title}", listing.Title);
                return;
            }

            var scored = candidates
                .Select(c => new { Candidate = c, Score = MatchScorer.ScoreMatch(parsed, c.ProductName, c.ConsoleName) })
                .OrderByDescending(x => x.Score)
                .First();

            confidence = scored.Score;

            if (confidence < MinConfidence)
            {
                await _listingsRepo.UpdateMatchStatusAsync(listing.ItemId, "NO_MATCH");
                _logger.LogDebug("NO_MATCH (low confidence {Score:F2}): {Title} -> best candidate: {Candidate}",
                    confidence, listing.Title, scored.Candidate.ProductName);
                return;
            }

            var detail = await _compsClient.GetProductAsync(scored.Candidate.Id);
            if (detail == null)
            {
                await _listingsRepo.UpdateMatchStatusAsync(listing.ItemId, "NO_MATCH");
                return;
            }

            compRecord = new CompCacheRecord
            {
                SearchQuery = parsed.CleanedForSearch,
                ProductId = detail.Id,
                ProductName = detail.ProductName,
                ConsoleName = detail.ConsoleName,
                LoosePrice = detail.LoosePrice,
                GradedPrice = detail.GradedPrice,
                NewPrice = detail.NewPrice,
                CibPrice = detail.CibPrice,
                ManualOnlyPrice = detail.ManualOnlyPrice,
                BoxOnlyPrice = detail.BoxOnlyPrice,
                Bgs10Price = detail.Bgs10Price,
                CompUrl = CompsClient.BuildSearchUrl(detail.ProductName, detail.ConsoleName),
                LastUpdated = DateTime.UtcNow
            };
            await _compCacheRepo.UpsertAsync(compRecord);
        }
        else
        {
            confidence = MatchScorer.ScoreMatch(parsed, compRecord.ProductName ?? "", compRecord.ConsoleName ?? "");
            if (confidence < MinConfidence)
            {
                await _listingsRepo.UpdateMatchStatusAsync(listing.ItemId, "NO_MATCH");
                return;
            }
        }

        var compPrice = CompPriceSelector.SelectPrice(compRecord, parsed.Grade);

        if (compPrice is null or 0)
        {
            await _listingsRepo.UpdateMatchStatusAsync(listing.ItemId, "NO_MATCH");
            _logger.LogDebug("NO_MATCH (no comp price for grade tier): {Title}", listing.Title);
            return;
        }

        var discountPct = (compPrice.Value - listing.CurrentPrice) / compPrice.Value * 100m;
        discountPct = Math.Clamp(discountPct, -99999.99m, 99999.99m);

        var buyShipping = listing.ShippingCost ?? EstimateBuyShipping(listing.CurrentPrice);
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