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
    private const double StrongTcgMatch = 0.8;
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
        if (TcgMatcher.IsTcg(card)) return await QuoteTcgAsync(card, productId);

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

    // Pokemon, Magic, Yu-Gi-Oh! etc. SportsCardsPro carries them with the same grade tiers as
    // sports cards; only the search and scoring differ (name + set + collector number + variant).
    private async Task<CardPriceQuote> QuoteTcgAsync(CardIdentification card, string? productId)
    {
        var query = TcgMatcher.FromCard(card);
        var grade = query.Grade;

        if (!string.IsNullOrWhiteSpace(productId))
        {
            var picked = await GetProductAsync($"product:{productId}", productId);
            return picked == null ? CardPriceQuote.None : BuildQuote(picked, grade, null, true, []);
        }

        // Most specific search first. PriceCharting returns at most ~10 results for a narrow
        // query, so when nothing scores well the next, looser query is tried too (each one is
        // another throttled call, so stop as soon as there's a confident match).
        var all = new Dictionary<string, CompAlternative>();
        string? usedQuery = null;
        foreach (var q in TcgMatcher.SearchQueries(query).Take(3))
        {
            var found = await _compsClient.SearchAsync(q, tradingCardGames: true);
            foreach (var c in found)
                all.TryAdd(c.Id, new CompAlternative(c.Id, c.ProductName, c.ConsoleName,
                    Math.Round(TcgMatcher.Score(query, c.ProductName, c.ConsoleName), 3)));
            if (found.Count > 0) usedQuery ??= q;
            if (all.Count > 0 && all.Values.Max(c => c.Score) >= StrongTcgMatch) break;
        }
        if (all.Count == 0 || usedQuery == null) return CardPriceQuote.None;

        var scored = all.Values
            .OrderByDescending(c => c.Score)
            .Take(MaxAlternatives)
            .ToList();

        var best = scored[0];
        if (best.Score < MinConfidence)
            return CardPriceQuote.None with { Alternatives = scored };

        // "tcg|" keeps these cache rows apart from sports title queries
        var comp = await GetProductAsync($"tcg|{usedQuery}", best.ProductId);
        return comp == null
            ? CardPriceQuote.None with { Alternatives = scored }
            : BuildQuote(comp, grade, best.Score, false, scored);
    }

    // Reads the product from COMP_CACHE when it's fresh and still the same product,
    // otherwise fetches it from SportsCardsPro and refreshes the cache row.
    private async Task<CompCacheRecord?> GetProductAsync(string cacheKey, string productId)
    {
        var cached = await _compCacheRepo.GetCachedAsync(cacheKey);
        if (cached != null && cached.ProductId == productId && cached.HasFullLadder && DateTime.Now - cached.LastUpdated < CacheMaxAge)
            return cached;

        var detail = await _compsClient.GetProductAsync(productId);
        if (detail == null) return cached?.ProductId == productId ? cached : null;

        var record = CompCacheRecord.FromDetail(cacheKey, detail);
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
            alternatives,
            comp.SalesVolume);
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
