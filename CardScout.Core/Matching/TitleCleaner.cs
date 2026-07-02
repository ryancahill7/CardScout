using System.Text.RegularExpressions;

namespace CardScout.Core.Matching;

public class ParsedTitle
{
    public string CleanedForSearch { get; set; } = "";
    public string? CardNumber { get; set; }
    public List<string> ParallelKeywords { get; set; } = new();
    public (string GradingCompany, decimal Grade)? Grade { get; set; }
}

public static class TitleCleaner
{
    // Common parallel/variant callouts — expand this list over time as you see real listings
    private static readonly string[] KnownParallels = new[]
    {
        "gold", "silver", "bronze", "chrome", "refractor", "prizm", "mojo",
        "black", "blue", "red", "green", "purple", "orange", "pink", "yellow",
        "rainbow foil", "rainbow", "ssp", "sp", "auto", "autograph", "patch",
        "numbered", "1/1", "independence day", "camo", "wave", "shimmer"
    };

    public static ParsedTitle Parse(string ebayTitle)
    {
        var result = new ParsedTitle();
        var working = ebayTitle;

        // 1. Extract grade BEFORE stripping it (need it, don't just discard)
        var gradeMatch = Regex.Match(working, @"\b(PSA|BGS|SGC|CGC)\s*(\d+(\.\d)?)\b", RegexOptions.IgnoreCase);
        if (gradeMatch.Success)
        {
            result.Grade = (gradeMatch.Groups[1].Value.ToUpper(), decimal.Parse(gradeMatch.Groups[2].Value));
        }
        working = Regex.Replace(working, @"\b(PSA|BGS|SGC|CGC|TAG)\s*\d+(\.\d)?\b", "", RegexOptions.IgnoreCase);

        // 2. Extract card number BEFORE stripping # (this is a strong signal, not noise)
        var cardNumMatch = Regex.Match(working, @"#([A-Z]{0,4}-?\d+[A-Z]?)", RegexOptions.IgnoreCase);
        if (cardNumMatch.Success)
        {
            result.CardNumber = cardNumMatch.Groups[1].Value.ToUpper();
        }

        // 3. Extract parallel/variant keywords BEFORE removing seller fluff
        var lowerWorking = working.ToLower();
        foreach (var parallel in KnownParallels)
        {
            if (Regex.IsMatch(lowerWorking, $@"\b{Regex.Escape(parallel)}\b"))
                result.ParallelKeywords.Add(parallel);
        }

        // 4. Now strip seller fluff for the search query itself
        working = Regex.Replace(working, @"\b(RC|MINT|NM|RARE|HOT|MUST SEE|L@@K|FREE SHIPPING)\b", "", RegexOptions.IgnoreCase);
        working = Regex.Replace(working, @"[*]", " ");
        working = Regex.Replace(working, @"\s+", " ").Trim();

        result.CleanedForSearch = working;
        return result;
    }
}