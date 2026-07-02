using System;
using System.Collections.Generic;
using System.Text;

namespace CardScout.Core.Models;


public class EbayListingRecord
{
    public string ItemId { get; set; } = "";
    public string Title { get; set; } = "";
    public decimal CurrentPrice { get; set; }
    public string? Condition { get; set; }
    public DateTime? EndTime { get; set; }
    public string ListingUrl { get; set; } = "";
    public DateTime FirstSeen { get; set; }
    public string MatchStatus { get; set; } = "";
    public string ListingType { get; set; } = "";
    public decimal? ShippingCost { get; set; }
}