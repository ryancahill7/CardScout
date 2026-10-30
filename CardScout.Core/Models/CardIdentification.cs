namespace CardScout.Core.Models;

public record CardIdentification(
    string? Player,
    int? Year,
    string? Manufacturer,
    string? Set,
    string? CardNumber,
    string? Parallel,
    string? SerialNumber,
    bool IsRookie,
    bool IsAutograph,
    bool IsRelic,
    string? GradingCompany,
    decimal? Grade,
    string? ConditionNotes,
    decimal Confidence,
    string SuggestedEbayTitle);

public record PriceTier(string Key, string Label, decimal? Price);

public record CompAlternative(string ProductId, string ProductName, string SetName, double Score);

// A SportsCardsPro price for a card we own. SuggestedPrice is the tier matching the
// card's grade; Tiers has every grade so the client can show the whole ladder.
// When no match clears the confidence bar, the price fields are null but Alternatives
// still lists the closest candidates so the user can pick one.
public record CardPriceQuote(
    decimal? SuggestedPrice,
    string? SelectedTier,
    decimal? NetProceeds,
    string? CompProductId,
    string? CompProductName,
    string? CompSetName,
    string? CompUrl,
    double? MatchConfidence,
    bool IsManualPick,
    DateTime? PricedAt,
    IReadOnlyList<PriceTier> Tiers,
    IReadOnlyList<CompAlternative> Alternatives)
{
    public static readonly CardPriceQuote None = new(
        null, null, null, null, null, null, null, null, false, null, [], []);
}
