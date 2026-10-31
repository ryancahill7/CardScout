using CardScout.Core.Ebay;
using CardScout.Core.Matching;
using CardScout.Core.Models;

namespace CardScout.Core.Flip;

public record MarketListing(
    string ItemId, string Title, decimal Price, decimal? Shipping, string ListingType,
    DateTime? EndTime, string Url, string? ImageUrl);

// What the same card is listed for on eBay right now. These are asking prices, not
// sales (the Browse API has no sold data), so they're a ceiling for what a buyer pays.
public record MarketSnapshot(
    string Query,
    int ActiveCount,
    decimal? LowestBuyItNow,
    decimal? MedianBuyItNow,
    decimal? HighestBuyItNow,
    int AuctionCount,
    IReadOnlyList<MarketListing> Cheapest)
{
    public static MarketSnapshot Empty(string query) => new(query, 0, null, null, null, 0, []);
}

public class MarketSnapshotService
{
    private readonly EbaySearchClient _ebay;

    public MarketSnapshotService(EbaySearchClient ebay)
    {
        _ebay = ebay;
    }

    public async Task<MarketSnapshot> GetAsync(CardIdentification card)
    {
        var query = BuildQuery(card);
        if (string.IsNullOrWhiteSpace(query)) return MarketSnapshot.Empty(query);

        var listings = await _ebay.SearchListingsAsync(query, null, "price", limit: 60);

        // eBay's keyword search is loose, so drop listings with a different grade or card number
        var wantedGrade = card.GradingCompany != null && card.Grade != null
            ? (card.GradingCompany.Trim().ToUpperInvariant(), card.Grade.Value)
            : ((string, decimal)?)null;
        var wantedNumber = card.CardNumber?.TrimStart('#').Trim().ToUpperInvariant();

        var year = card.Year?.ToString();
        var relevant = listings.Where(l =>
        {
            if (MultiCardListing.IsMatch(l.Title)) return false;
            if (year != null && !l.Title.Contains(year)) return false;
            var parsed = TitleCleaner.Parse(l.Title);
            if (wantedGrade == null ? parsed.Grade != null : parsed.Grade != wantedGrade) return false;
            if (!string.IsNullOrEmpty(wantedNumber) && parsed.CardNumber != null && parsed.CardNumber != wantedNumber) return false;
            if (!card.IsAutograph && parsed.ParallelKeywords.Contains("auto")) return false;
            return l.CurrentPrice > 0;
        }).ToList();

        var bin = relevant.Where(l => l.ListingType == "FIXED_PRICE").Select(l => l.CurrentPrice).OrderBy(p => p).ToList();

        return new MarketSnapshot(
            query,
            relevant.Count,
            bin.Count > 0 ? bin[0] : null,
            bin.Count > 0 ? Median(bin) : null,
            bin.Count > 0 ? bin[^1] : null,
            relevant.Count(l => l.ListingType == "AUCTION"),
            relevant
                .OrderBy(l => l.CurrentPrice + (l.ShippingCost ?? 0))
                .Take(5)
                .Select(l => new MarketListing(l.ItemId, l.Title, l.CurrentPrice, l.ShippingCost, l.ListingType,
                    l.EndTime, l.ItemUrl, l.ImageUrl))
                .ToList());
    }

    // "Pick your card", "you pick", lots and bundles aren't priced like a single card
    private static readonly System.Text.RegularExpressions.Regex MultiCardListing = new(
        @"\b(pick|choose|u pick|lot|lots|bundle|complete set|team set|\d+\s*cards?)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static decimal Median(List<decimal> sorted) =>
        sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;

    public static string BuildQuery(CardIdentification card)
    {
        var parts = new List<string?>
        {
            card.Year?.ToString(), card.Manufacturer, card.Set, card.Player, card.Parallel,
            string.IsNullOrWhiteSpace(card.CardNumber) ? null : "#" + card.CardNumber.TrimStart('#'),
            card.IsAutograph ? "auto" : null,
            card.GradingCompany != null && card.Grade != null ? $"{card.GradingCompany} {card.Grade:0.#}" : null
        };
        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
