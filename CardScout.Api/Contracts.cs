using CardScout.Core.Models;

namespace CardScout.Api;

public record IdentifyResponse(
    CardIdentification Card,
    CardPriceQuote Price,
    // Set when pricing failed (e.g. SportsCardsPro is down); the identification still succeeded
    string? PriceError,
    string FrontImagePath,
    string BackImagePath,
    string AiRawJson);

// Card fields to price, plus an optional SportsCardsPro product to pin the quote to
public record PriceQuoteRequest(
    string? Player,
    int? Year,
    string? Manufacturer,
    string? Set,
    string? CardNumber,
    string? Parallel,
    bool IsAutograph,
    string? GradingCompany,
    decimal? Grade,
    string? CompProductId)
{
    public CardIdentification ToIdentification() => new(
        Player, Year, Manufacturer, Set, CardNumber, Parallel, null,
        false, IsAutograph, false, GradingCompany, Grade, null, 0, "");
}

public record CreateCardRequest(
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
    decimal? Confidence,
    string EbayTitle,
    decimal? SuggestedPrice,
    string? CompProductId,
    decimal? CostBasis,
    string? Status,
    string? FrontImagePath,
    string? BackImagePath,
    string? AiRawJson);

public record CardDto(
    long Id,
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
    decimal? Confidence,
    string EbayTitle,
    decimal? SuggestedPrice,
    string? CompProductId,
    decimal? CostBasis,
    string Status,
    decimal? ListedPrice,
    decimal? SoldPrice,
    DateTime? SoldAt,
    string? FrontImageUrl,
    string? BackImageUrl,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static CardDto From(CardInventoryItem c) => new(
        c.Id, c.Player, c.CardYear, c.Manufacturer, c.SetName, c.CardNumber, c.Parallel, c.SerialNumber,
        c.IsRookie == "Y", c.IsAutograph == "Y", c.IsRelic == "Y",
        c.GradingCompany, c.Grade, c.ConditionNotes, c.AiConfidence, c.EbayTitle, c.SuggestedPrice,
        c.CompProductId, c.CostBasis, c.Status, c.ListedPrice, c.SoldPrice, c.SoldAt,
        c.FrontImagePath == null ? null : $"/api/cards/{c.Id}/images/front",
        c.BackImagePath == null ? null : $"/api/cards/{c.Id}/images/back",
        c.CreatedAt, c.UpdatedAt);
}
