using System;
using System.Collections.Generic;
using System.Text;

namespace CardScout.Core.Models;

public class Deal
{
    public string ListingId { get; set; } = "";
    public string Title { get; set; } = "";
    public decimal CurrentPrice { get; set; }
    public decimal CompPrice { get; set; }
    public decimal DiscountPct { get; set; }
    public DateTime EndTime { get; set; }
    public string ListingUrl { get; set; } = "";
    public DateTime FirstSeen { get; set; }
}