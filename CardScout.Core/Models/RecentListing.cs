using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace CardScout.Core.Models;

public class RecentListing
{
    public string ItemId { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public string Title { get; set; } = "";
    public decimal Price { get; set; }
    public string ListingType { get; set; } = "";
    public string? Condition { get; set; }
    public string ListingUrl { get; set; } = "";
    public DateTime FirstSeen { get; set; }
}