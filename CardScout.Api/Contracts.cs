using CardScout.Core.Flip;
using CardScout.Core.Models;

namespace CardScout.Api;

public record IdentifyResponse(
    CardIdentification Card,
    CardPriceQuote Price,
    // Set when pricing failed (e.g. SportsCardsPro is down); the identification still succeeded
    string? PriceError,
    string FrontImagePath,
    // Null for a front-only (show mode) scan
    string? BackImagePath,
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
    string? AiRawJson,
    string? PurchaseSource = null,
    DateTime? PurchasedAt = null,
    decimal? GradingCost = null,
    string? CertNumber = null);

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
    string? PurchaseSource,
    DateTime? PurchasedAt,
    decimal? GradingCost,
    decimal? SaleShippingCost,
    decimal? MarketPrice,
    DateTime? MarketPriceAt,
    string? CertNumber,
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
        c.PurchaseSource, c.PurchasedAt, c.GradingCost, c.SaleShippingCost, c.MarketPrice, c.MarketPriceAt,
        c.CertNumber, c.CreatedAt, c.UpdatedAt);
}

// ---- Lots ----

public record LotCardResult(LotCardIdentification Card, CardPriceQuote Price, decimal? NetProceeds, string? PriceError);

public record LotAnalysisResponse(
    IReadOnlyList<LotCardResult> Cards,
    string? Notes,
    decimal TotalMarketValue,
    int PricedCount,
    FlipAnalysis? Flip,
    string PhotoPath);

// ---- Watchlists ----

public record WatchlistRequest(
    string Name,
    string Query,
    string? ListingType,
    decimal? MaxPrice,
    decimal? MinProfit,
    decimal? MinProfitPct,
    bool GradedOnly,
    bool Notify = true,
    bool IsActive = true);

public record WatchlistDto(
    long Id, string Name, string Query, string ListingType, decimal? MaxPrice, decimal MinProfit, decimal MinProfitPct,
    bool GradedOnly, bool Notify, bool IsActive, DateTime? LastRunAt, DateTime CreatedAt)
{
    public static WatchlistDto From(Watchlist w) => new(
        w.Id, w.Name, w.Query, w.ListingType, w.MaxPrice, w.MinProfit, w.MinProfitPct,
        w.GradedOnly == "Y", w.Notify == "Y", w.IsActive == "Y", w.LastRunAt, w.CreatedAt);
}

public record WatchlistHitDto(
    long Id, long WatchlistId, string? WatchlistName, string ItemId, string Title, string? ListingType,
    decimal Price, decimal? ShippingCost, bool ShippingEstimated, DateTime? EndTime, string? ListingUrl, string? ImageUrl,
    string? CompProductName, string? CompSetName, decimal? CompPrice, string? CompTier, decimal? MatchConfidence,
    decimal? NetProfit, decimal? NetProfitPct, decimal? MaxBuyPrice, bool IsProfitable, string Status,
    DateTime CreatedAt)
{
    public static WatchlistHitDto From(WatchlistHit h) => new(
        h.Id, h.WatchlistId, h.WatchlistName, h.ItemId, h.Title, h.ListingType, h.Price, h.ShippingCost,
        h.ShippingEstimated == "Y",
        // eBay end times are stored in UTC; mark them so clients don't read them as local time
        h.EndTime == null ? null : DateTime.SpecifyKind(h.EndTime.Value, DateTimeKind.Utc),
        h.ListingUrl, h.ImageUrl, h.CompProductName, h.CompSetName, h.CompPrice, h.CompTier, h.MatchConfidence,
        h.NetProfit, h.NetProfitPct, h.MaxBuyPrice, h.IsProfitable == "Y", h.UserStatus, h.CreatedAt);
}

public record HitStatusRequest(string Status);

// ---- Market deals (the global DEALS grid) ----

public record MarketDealDto(
    string ItemId, string Title, string? ListingType, decimal Price, decimal? ShippingCost, string? Condition,
    DateTime? EndTime, string Url, string? CompName, string? CompSetName, string? CompUrl, decimal CompPrice,
    decimal DiscountPct, decimal NetProfit, decimal NetProfitPct, decimal MatchConfidence, DateTime FoundAt);

// ---- Push ----

public record PushTokenRequest(string Token, string? Platform);

// ---- Portfolio ----

public record PortfolioHeld(int Count, int PricedCount, decimal MarketValue, decimal CostBasis, decimal UnrealizedProfit);

public record PortfolioSold(
    int Count, decimal Revenue, decimal Fees, decimal Shipping, decimal CostBasis, decimal NetProfit);

public record PortfolioMonth(string Month, int Sold, decimal Revenue, decimal NetProfit, decimal Spent);

public record PortfolioMover(
    long CardId, string Title, string? Player, decimal From, decimal To, decimal ChangePct, string? FrontImageUrl);

public record PortfolioResponse(
    PortfolioHeld Held, PortfolioSold Sold, IReadOnlyList<PortfolioMonth> Monthly, IReadOnlyList<PortfolioMover> Movers);
