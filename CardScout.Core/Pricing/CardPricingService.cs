using CardScout.Core.Comps;
using CardScout.Core.Data;
using CardScout.Core.Matching;
using CardScout.Core.Models;

namespace CardScout.Core.Pricing;

// Prices a card we own by matching it to SportsCardsPro the same way MatcherService
// matches eBay listings (TitleCleaner -> CompsClient -> MatchScorer), with a few extras
// the app needs: the full grade ladder, runner-up candidates the user can switch to,
// and a refresh of cached prices that have gone stale.
public class CardPricingService
{
    private const double MinConfidence = 0.55;
    private const int MaxAlternatives = 6;
    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromDays(3);

    private readonly CompCacheRepository _compCacheRepo;
    private readonly CompsClient _compsClient;

    public CardPricingService(CompCacheRepository compCacheRepo, CompsClient compsClient)
    {
        _compCacheRepo = compCacheRepo;
        _compsClient = compsClient;
    }

    // productId pins the quote to a specific SportsCardsPro product (the user picked an
    // alternative); otherwise the best-scoring search result is used.
    public async Task<CardPriceQuote> QuoteAsync(CardIdentification card, string? productId = null)
    {
        var parsed = TitleCleaner.Parse(BuildSearchTitle(card, includeParallel: true));
        var grade = card.GradingCompany != null && card.Grade != null
            ? (card.GradingCompany.Trim().ToUpperInvariant(), card.Grade.Value)
            : parsed.Grade;

        if (!string.IsNullOrWhiteSpace(productId))
        {
            var picked = await GetProductAsync($"product:{productId}", productId);
            return picked == null ? CardPriceQuote.None : BuildQuote(picked, grade, null, true, []);
        }

        if (string.IsNullOrWhiteSpace(parsed.CleanedForSearch)) return CardPriceQuote.None;

        var candidates = await _compsClient.SearchAsync(parsed.CleanedForSearch);

        // A misread or unusual parallel name often returns nothing; retry without it
        if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(card.Parallel))
        {
            var loose = TitleCleaner.Parse(BuildSearchTitle(card, includeParallel: false));
            if (!string.IsNullOrWhiteSpace(loose.CleanedForSearch))
                candidates = await _compsClient.SearchAsync(loose.CleanedForSearch);
        }
        if (candidates.Count == 0) return CardPriceQuote.None;

        var scored = candidates
            .Select(c => new CompAlternative(c.Id, c.ProductName, c.ConsoleName,
                Math.Round(MatchScorer.ScoreMatch(parsed, c.ProductName, c.ConsoleName), 3)))
            .OrderByDescending(c => c.Score)
            .Take(MaxAlternatives)
            .ToList();

        var best = scored[0];
        if (best.Score < MinConfidence)
            return CardPriceQuote.None with { Alternatives = scored };

        var comp = await GetProductAsync(parsed.CleanedForSearch, best.ProductId);
        return comp == null
            ? CardPriceQuote.None with { Alternatives = scored }
            : BuildQuote(comp, grade, best.Score, false, scored);
    }

    // Reads the product from COMP_CACHE when it's fresh and still the same product,
    // otherwise fetches it from SportsCardsPro and refreshes the cache row.
    private async Task<CompCacheRecord?> GetProductAsync(string cacheKey, string productId)
    {
        var cached = await _compCacheRepo.GetCachedAsync(cacheKey);
        if (cached != null && cached.ProductId == productId && DateTime.Now - cached.LastUpdated < CacheMaxAge)
            return cached;

        var detail = await _compsClient.GetProductAsync(productId);
        if (detail == null) return cached?.ProductId == productId ? cached : null;

        var record = new CompCacheRecord
        {
            SearchQuery = cacheKey,
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
            LastUpdated = DateTime.Now
        };
        await _compCacheRepo.UpsertAsync(record);
        return record;
    }

    private static CardPriceQuote BuildQuote(
        CompCacheRecord comp, (string GradingCompany, decimal Grade)? grade, double? confidence,
        bool manual, IReadOnlyList<CompAlternative> alternatives)
    {
        var tiers = CompPriceSelector.Tiers
            .Select(t => new PriceTier(t.Key, t.Label, t.Price(comp) is > 0 and var p ? p : null))
            .ToList();
        var selected = CompPriceSelector.SelectTier(grade);
        var price = tiers.First(t => t.Key == selected).Price;

        return new CardPriceQuote(
            price,
            selected,
            price == null ? null : Math.Round(SellerFees.NetProceeds(price.Value), 2),
            comp.ProductId,
            comp.ProductName,
            comp.ConsoleName,
            comp.CompUrl,
            confidence,
            manual,
            comp.LastUpdated,
            tiers,
            alternatives);
    }

    // Builds an eBay-style title from structured fields so TitleCleaner can pick up
    // the card number (#xx) and grade (PSA 10) the same way it does for listings.
    private static string BuildSearchTitle(CardIdentification card, bool includeParallel)
    {
        var parts = new List<string?>
        {
            card.Year?.ToString(), card.Manufacturer, card.Set, card.Player,
            includeParallel ? card.Parallel : null,
            string.IsNullOrWhiteSpace(card.CardNumber) ? null : "#" + card.CardNumber.TrimStart('#'),
            card.IsAutograph ? "Auto" : null,
            card.GradingCompany != null && card.Grade != null ? $"{card.GradingCompany} {card.Grade:0.#}" : null
        };
        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
