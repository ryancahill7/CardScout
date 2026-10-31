namespace CardScout.Core.Models;

public class Watchlist
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Name { get; set; } = "";
    public string Query { get; set; } = "";
    // ANY, AUCTION or FIXED_PRICE
    public string ListingType { get; set; } = WatchlistListingType.Any;
    public decimal? MaxPrice { get; set; }
    public decimal MinProfit { get; set; } = 5m;
    public decimal MinProfitPct { get; set; } = 20m;
    public string GradedOnly { get; set; } = "N";
    public string Notify { get; set; } = "Y";
    public string IsActive { get; set; } = "Y";
    public DateTime? LastRunAt { get; set; }
    // "sports" or "tcg" (null = sports): picks the eBay category and how titles are matched
    public string? Category { get; set; }
    // TCG game hint (pokemon, mtg, ...); titles usually name the game anyway
    public string? Game { get; set; }
    public DateTime CreatedAt { get; set; }
}

public static class WatchlistListingType
{
    public const string Any = "ANY";
    public const string Auction = "AUCTION";
    public const string FixedPrice = "FIXED_PRICE";

    public static readonly string[] All = { Any, Auction, FixedPrice };
}

public class WatchlistHit
{
    public long Id { get; set; }
    public long WatchlistId { get; set; }
    public long UserId { get; set; }
    public string? WatchlistName { get; set; }
    public string ItemId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? ListingType { get; set; }
    public decimal Price { get; set; }
    public decimal? ShippingCost { get; set; }
    public string ShippingEstimated { get; set; } = "N";
    public DateTime? EndTime { get; set; }
    public string? ListingUrl { get; set; }
    public string? ImageUrl { get; set; }
    public string? CompProductId { get; set; }
    public string? CompProductName { get; set; }
    public string? CompSetName { get; set; }
    public decimal? CompPrice { get; set; }
    public string? CompTier { get; set; }
    public decimal? MatchConfidence { get; set; }
    public decimal? NetProfit { get; set; }
    public decimal? NetProfitPct { get; set; }
    public decimal? MaxBuyPrice { get; set; }
    public string IsProfitable { get; set; } = "N";
    public string UserStatus { get; set; } = WatchlistHitStatus.New;
    public DateTime? NotifiedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public static class WatchlistHitStatus
{
    public const string New = "NEW";
    public const string Saved = "SAVED";
    public const string Dismissed = "DISMISSED";
    public const string Bought = "BOUGHT";

    public static readonly string[] All = { New, Saved, Dismissed, Bought };
}

public class PriceHistoryPoint
{
    public long InventoryId { get; set; }
    public decimal Price { get; set; }
    public DateTime CapturedAt { get; set; }
}
