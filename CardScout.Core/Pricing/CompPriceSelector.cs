using CardScout.Core.Models;

namespace CardScout.Core.Pricing;

public static class CompPriceSelector
{
    // SportsCardsPro reuses video-game price field names for card grade tiers
    public static readonly (string Key, string Label, Func<CompCacheRecord, decimal?> Price)[] Tiers =
    {
        ("raw", "Raw", c => c.LoosePrice),
        ("g7", "Grade 7", c => c.CibPrice),
        ("g8", "Grade 8", c => c.NewPrice),
        ("g9", "Grade 9", c => c.GradedPrice),
        ("g95", "Grade 9.5", c => c.BoxOnlyPrice),
        ("psa10", "PSA 10", c => c.ManualOnlyPrice),
        ("bgs10", "BGS 10", c => c.Bgs10Price)
    };

    public static string SelectTier((string GradingCompany, decimal Grade)? grade)
    {
        return grade switch
        {
            { GradingCompany: "PSA", Grade: 10 } => "psa10",
            { GradingCompany: "BGS", Grade: 10 } => "bgs10",
            { Grade: >= 9.5m } => "g95",
            { Grade: >= 9 } => "g9",
            { Grade: >= 8 } => "g8",
            { Grade: >= 7 } => "g7",
            _ => "raw"
        };
    }

    public static decimal? SelectPrice(CompCacheRecord comp, (string GradingCompany, decimal Grade)? grade)
    {
        var key = SelectTier(grade);
        return Tiers.First(t => t.Key == key).Price(comp);
    }
}
