using System;
using System.Collections.Generic;
using System.Text;

namespace CardScout.Core.Models;

public class DealRecord
{
    public string ItemId { get; set; } = "";
    public string CompProductId { get; set; } = "";
    public decimal CompPriceUsed { get; set; }
    public decimal DiscountPct { get; set; }
    public decimal MatchConfidence { get; set; }
    public decimal NetProfit { get; set; }
    public decimal NetProfitPct { get; set; }
    public string IsProfitable { get; set; } = "Y";
    public DateTime CreatedAt { get; set; }
}