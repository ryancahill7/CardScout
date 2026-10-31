namespace CardScout.Core.Models;

public class CardInventoryItem
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string? Player { get; set; }
    public int? CardYear { get; set; }
    public string? Manufacturer { get; set; }
    public string? SetName { get; set; }
    public string? CardNumber { get; set; }
    public string? Parallel { get; set; }
    public string? SerialNumber { get; set; }
    public string IsRookie { get; set; } = "N";
    public string IsAutograph { get; set; } = "N";
    public string IsRelic { get; set; } = "N";
    public string? GradingCompany { get; set; }
    public string? CertNumber { get; set; }
    public decimal? Grade { get; set; }
    public string? ConditionNotes { get; set; }
    public decimal? AiConfidence { get; set; }
    public string EbayTitle { get; set; } = "";
    public decimal? SuggestedPrice { get; set; }
    public string? CompProductId { get; set; }
    public decimal? CostBasis { get; set; }
    public string Status { get; set; } = CardStatus.Unlisted;
    public decimal? ListedPrice { get; set; }
    public decimal? SoldPrice { get; set; }
    public DateTime? SoldAt { get; set; }
    public string? FrontImagePath { get; set; }
    public string? BackImagePath { get; set; }
    public string? AiRawJson { get; set; }
    public string? PurchaseSource { get; set; }
    public DateTime? PurchasedAt { get; set; }
    public decimal? GradingCost { get; set; }
    public decimal? SaleShippingCost { get; set; }
    // Latest comp from PortfolioPriceWorker; SuggestedPrice is the user's own asking price
    public decimal? MarketPrice { get; set; }
    public DateTime? MarketPriceAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public static class CardStatus
{
    public const string Unlisted = "UNLISTED";
    public const string Listed = "LISTED";
    public const string Sold = "SOLD";

    public static readonly string[] All = { Unlisted, Listed, Sold };
}
