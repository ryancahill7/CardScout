using System;
using System.Collections.Generic;
using System.Text;

namespace CardScout.Core.Models;

public class CompCacheRecord
{
    public string SearchQuery { get; set; } = "";
    public string? ProductId { get; set; }
    public string? ProductName { get; set; }
    public string? ConsoleName { get; set; }
    public decimal? LoosePrice { get; set; }
    public decimal? GradedPrice { get; set; }
    public decimal? NewPrice { get; set; }
    public decimal? CibPrice { get; set; }
    public decimal? ManualOnlyPrice { get; set; }
    public decimal? BoxOnlyPrice { get; set; }
    public decimal? Bgs10Price { get; set; }
    public string? CompUrl { get; set; }         
    public DateTime LastUpdated { get; set; }
}