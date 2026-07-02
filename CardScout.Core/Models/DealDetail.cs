using System;
using System.Collections.Generic;
using System.Text;

namespace CardScout.Core.Models;

public class DealDetail
{
    public string ItemId { get; set; } = "";
    public string EbayTitle { get; set; } = "";
    public string? ListingType { get; set; }
    public decimal EbayPrice { get; set; }
    public decimal? ShippingCost { get; set; }
    public string? EbayCondition { get; set; }
    public DateTime? AuctionEndTimeLocal { get; set; }
    public string EbayUrl { get; set; } = "";
    public string? CompCardName { get; set; }
    public string? CompSetName { get; set; }
    public decimal CompPrice { get; set; }
    public decimal DiscountPct { get; set; }
    public decimal MatchConfidence { get; set; }
    public DateTime DealFoundAt { get; set; }
    public string? CompUrl { get; set; }
}
