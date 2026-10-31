using CardScout.Core.Models;

namespace CardScout.Core.Pricing;

public static class CompPriceSelector
{
    // SportsCardsPro reuses video-game price field names for card grade tiers, plus
    // condition-N fields for grades 1-6 and the CGC / SGC 10 tiers. Ordered low to high.
    public static readonly (string Key, string Label, Func<CompCacheRecord, decimal?> Price)[] Tiers =
    {
        ("raw", "Raw", c => c.LoosePrice),
        ("g1", "Grade 1", c => c.Grade1Price),
        ("g2", "Grade 2", c => c.Grade2Price),
        ("g3", "Grade 3", c => c.Grade3Price),
        ("g4", "Grade 4", c => c.Grade4Price),
        ("g5", "Grade 5", c => c.Grade5Price),
        ("g6", "Grade 6", c => c.Grade6Price),
        ("g7", "Grade 7", c => c.CibPrice),
        ("g8", "Grade 8", c => c.NewPrice),
        ("g9", "Grade 9", c => c.GradedPrice),
        ("g95", "Grade 9.5", c => c.BoxOnlyPrice),
        ("sgc10", "SGC 10", c => c.Sgc10Price),
        ("cgc10", "CGC 10", c => c.Cgc10Price),
        ("psa10", "PSA 10", c => c.ManualOnlyPrice),
        ("bgs10", "BGS 10", c => c.Bgs10Price)
    };

    // Half grades (6.5, 8.5) sell close to the grade below, so they use that tier
    public static string SelectTier((string GradingCompany, decimal Grade)? grade)
    {
        return grade switch
        {
            null => "raw",
            { GradingCompany: "PSA", Grade: 10 } => "psa10",
            { GradingCompany: "BGS", Grade: 10 } => "bgs10",
            { GradingCompany: "SGC", Grade: 10 } => "sgc10",
            { GradingCompany: "CGC", Grade: 10 } => "cgc10",
            { Grade: >= 9.5m } => "g95",
            { Grade: >= 9 } => "g9",
            { Grade: >= 8 } => "g8",
            { Grade: >= 7 } => "g7",
            { Grade: >= 6 } => "g6",
            { Grade: >= 5 } => "g5",
            { Grade: >= 4 } => "g4",
            { Grade: >= 3 } => "g3",
            { Grade: >= 2 } => "g2",
            { Grade: >= 1 } => "g1",
            _ => "raw"
        };
    }

    public static decimal? SelectPrice(CompCacheRecord comp, (string GradingCompany, decimal Grade)? grade)
    {
        var key = SelectTier(grade);
        return Tiers.First(t => t.Key == key).Price(comp);
    }
}
