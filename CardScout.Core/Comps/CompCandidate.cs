using System;
using System.Collections.Generic;
using System.Text;

namespace CardScout.Core.Comps;

public class CompCandidate
{
    public string Id { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string ConsoleName { get; set; } = "";
}

public class CompDetail
{
    public string Id { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string ConsoleName { get; set; } = "";
    public decimal? LoosePrice { get; set; }
    public decimal? GradedPrice { get; set; }
    public decimal? NewPrice { get; set; }
    public decimal? CibPrice { get; set; }
    public decimal? ManualOnlyPrice { get; set; }
    public decimal? BoxOnlyPrice { get; set; }
    public decimal? Bgs10Price { get; set; }
    // Undocumented condition-N fields: grades 1-6 and the CGC / SGC 10 tiers
    public decimal? Grade1Price { get; set; }
    public decimal? Grade2Price { get; set; }
    public decimal? Grade3Price { get; set; }
    public decimal? Grade4Price { get; set; }
    public decimal? Grade5Price { get; set; }
    public decimal? Grade6Price { get; set; }
    public decimal? Cgc10Price { get; set; }
    public decimal? Sgc10Price { get; set; }
    public int? SalesVolume { get; set; }
}