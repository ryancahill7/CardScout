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
    public decimal? Grade1Price { get; set; }
    public decimal? Grade2Price { get; set; }
    public decimal? Grade3Price { get; set; }
    public decimal? Grade4Price { get; set; }
    public decimal? Grade5Price { get; set; }
    public decimal? Grade6Price { get; set; }
    public decimal? Cgc10Price { get; set; }
    public decimal? Sgc10Price { get; set; }
    public int? SalesVolume { get; set; }
    public string? CompUrl { get; set; }
    public DateTime LastUpdated { get; set; }

    // Rows cached before grades 1-6 were stored need one refresh to get them
    public static readonly DateTime FullLadderSince = new(2026, 10, 31, 12, 0, 0);
    public bool HasFullLadder => LastUpdated >= FullLadderSince;

    public static CompCacheRecord FromDetail(string searchQuery, Comps.CompDetail d) => new()
    {
        SearchQuery = searchQuery,
        ProductId = d.Id,
        ProductName = d.ProductName,
        ConsoleName = d.ConsoleName,
        LoosePrice = d.LoosePrice,
        GradedPrice = d.GradedPrice,
        NewPrice = d.NewPrice,
        CibPrice = d.CibPrice,
        ManualOnlyPrice = d.ManualOnlyPrice,
        BoxOnlyPrice = d.BoxOnlyPrice,
        Bgs10Price = d.Bgs10Price,
        Grade1Price = d.Grade1Price,
        Grade2Price = d.Grade2Price,
        Grade3Price = d.Grade3Price,
        Grade4Price = d.Grade4Price,
        Grade5Price = d.Grade5Price,
        Grade6Price = d.Grade6Price,
        Cgc10Price = d.Cgc10Price,
        Sgc10Price = d.Sgc10Price,
        SalesVolume = d.SalesVolume,
        CompUrl = Comps.CompsClient.BuildSearchUrl(d.ProductName, d.ConsoleName),
        LastUpdated = DateTime.Now
    };
}