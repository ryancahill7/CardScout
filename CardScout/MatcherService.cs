using CardScout.Core.Comps;
using CardScout.Core.Data;
using CardScout.Core.Matching;
using CardScout.Core.Models;

namespace CardScout;

public class MatcherService : BackgroundService
{
    private readonly ILogger<MatcherService> _logger;
    private readonly EbayListingsRepository _listingsRepo;
    private readonly CompCacheRepository _compCacheRepo;
    private readonly DealsRepository _dealsRepo;
    private readonly CompsClient _compsClient;

    private const double MinConfidence = 0.80;
    private const decimal MinDiscountPct = 10m;

    public MatcherService(
        ILogger<MatcherService> logger,
        EbayListingsRepository listingsRepo,
        CompCacheRepository compCacheRepo,
        DealsRepository dealsRepo,
        CompsClient compsClient)
    {
        _logger = logger;
        _listingsRepo = listingsRepo;
        _compCacheRepo = compCacheRepo;
        _dealsRepo = dealsRepo;
        _compsClient = compsClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var pending = await _listingsRepo.GetPendingAsync(10);

            foreach (var listing in pending)
            {
                try
                {
                    await ProcessListingAsync(listing);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process listing {ItemId}", listing.ItemId);
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

        decimal? compPrice = parsed.Grade switch
        {
            { GradingCompany: "PSA", Grade: 10 } => compRecord.ManualOnlyPrice,
            { GradingCompany: "BGS", Grade: 10 } => compRecord.Bgs10Price,
            { Grade: >= 9.5m } => compRecord.BoxOnlyPrice,
            { Grade: >= 9 } => compRecord.GradedPrice,
            { Grade: >= 8 } => compRecord.NewPrice,
            { Grade: >= 7 } => compRecord.CibPrice,
            _ => compRecord.LoosePrice
        };

        if (compPrice is null or 0)
        {
            await _listingsRepo.UpdateMatchStatusAsync(listing.ItemId, "NO_MATCH");
            _logger.LogDebug("NO_MATCH (no comp price for grade tier): {Title}", listing.Title);
            return;
        }

        var discountPct = (compPrice.Value - listing.CurrentPrice) / compPrice.Value * 100m;

        if (discountPct >= MinDiscountPct)
        {
            await _dealsRepo.InsertDealAsync(new DealRecord
            {
                ItemId = listing.ItemId,
                CompProductId = compRecord.ProductId ?? "",
                CompPriceUsed = compPrice.Value,
                DiscountPct = discountPct,
                MatchConfidence = (decimal)confidence,
                CreatedAt = DateTime.UtcNow
            });
            _logger.LogInformation(
                "DEAL FOUND ({Confidence:F2} confidence): {Title} — {Discount:F1}% under comp (${Comp} comp vs ${Price} price) — matched to {CompName}",
                confidence, listing.Title, discountPct, compPrice.Value, listing.CurrentPrice, compRecord.ProductName);
        }
        else
        {
            _logger.LogDebug("MATCHED but below threshold ({Discount:F1}%): {Title}", discountPct, listing.Title);
        }

        await _listingsRepo.UpdateMatchStatusAsync(listing.ItemId, "MATCHED");
    }
}