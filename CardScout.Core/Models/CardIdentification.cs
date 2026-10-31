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
    string SuggestedEbayTitle,
    // AI's read of how a raw card would grade; null for slabbed cards and old scans
    GradeOutlook? GradeOutlook = null,
    // Grading company certification number from the slab label (not the /99 serial numbering)
    string? CertNumber = null);

// Probabilities (0-1, summing to ~1) that a raw card grades PSA 10 / 9 / 8 / 7 or lower,
// judged from centering, corners, edges and surface in the photos
public record GradeOutlook(decimal Gem10, decimal Mint9, decimal NearMint8, decimal Lower, string? Summary);

// One card found in a photo of several cards (a lot, a binder page, a dealer's case)
public record LotCardIdentification(
    string? Player,
    int? Year,
    string? Manufacturer,
    string? Set,
    string? CardNumber,
    string? Parallel,
    bool IsRookie,
    bool IsAutograph,
    string? GradingCompany,
    decimal? Grade,
    decimal Confidence,
    string? Position)
{
    public CardIdentification ToIdentification() => new(
        Player, Year, Manufacturer, Set, CardNumber, Parallel, null,
        IsRookie, IsAutograph, false, GradingCompany, Grade, null, Confidence, "");
}

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
    IReadOnlyList<CompAlternative> Alternatives,
    // Sales SportsCardsPro has recorded for this card across all grades; the comps are built from them
    int? SalesVolume = null)
{
    public static readonly CardPriceQuote None = new(
        null, null, null, null, null, null, null, null, false, null, [], []);
}

public static class CardIdentificationExtensions
{
    public static CardIdentification ToIdentification(this CardInventoryItem c) => new(
        c.Player, c.CardYear, c.Manufacturer, c.SetName, c.CardNumber, c.Parallel, c.SerialNumber,
        c.IsRookie == "Y", c.IsAutograph == "Y", c.IsRelic == "Y", c.GradingCompany, c.Grade,
        c.ConditionNotes, c.AiConfidence ?? 0, c.EbayTitle, null, c.CertNumber);
}
