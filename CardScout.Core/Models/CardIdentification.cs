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
    string? CertNumber = null,
    // "sports" or "tcg" (null = sports, for older scans). For TCG cards, Player holds the card
    // name ("Charizard"), Set the set/expansion, CardNumber the collector number as printed
    // ("4/102", "LOB-EN001"), Parallel the variant/finish ("1st Edition", "Reverse Holo") and
    // Manufacturer the publisher.
    string? Category = null,
    // pokemon, mtg, yugioh, one-piece, lorcana or other (TCG only)
    string? Game = null,
    // Printed set code, e.g. "LOB", "OP01", "MH3", "SV3PT5"
    string? SetCode = null,
    // As printed or shown by the rarity symbol, e.g. "Rare Holo", "Special Illustration Rare", "Mythic"
    string? Rarity = null,
    // Card language; null or "English" for English cards
    string? Language = null)
{
    public bool IsTcg => string.Equals(Category, "tcg", StringComparison.OrdinalIgnoreCase);
}

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
    string? Position,
    // Same meaning as on CardIdentification; null for sports cards and older responses
    string? Category = null,
    string? Game = null,
    string? Language = null)
{
    public CardIdentification ToIdentification() => new(
        Player, Year, Manufacturer, Set, CardNumber, Parallel, null,
        IsRookie, IsAutograph, false, GradingCompany, Grade, null, Confidence, "",
        Category: Category, Game: Game, Language: Language);
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
        c.ConditionNotes, c.AiConfidence ?? 0, c.EbayTitle, null, c.CertNumber,
        c.Category, c.Game, c.SetCode, c.Rarity, c.CardLanguage);
}
