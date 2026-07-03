using System.Text.RegularExpressions;

namespace CardScout.Core.Matching;

public class ParsedTitle
{
    public string CleanedForSearch { get; set; } = "";
    public string? CardNumber { get; set; }
    public List<string> ParallelKeywords { get; set; } = new();
    public string? ColorModifier { get; set; }  // e.g. "blue", "red" — only when paired with a parallel base term
    public (string GradingCompany, decimal Grade)? Grade { get; set; }
}

public static class TitleCleaner
{
    private static readonly string[] KnownParallels = new[]
    {
        "gold", "chrome", "refractor", "prizm", "mojo", "checkerboard",
        "rainbow foil", "rainbow", "ssp", "auto", "autograph", "patch",
        "independence day", "camo", "wave", "shimmer"
    };

    // Colors only count as a real parallel signal when directly adjacent to a
    // parallel base term (e.g. "Blue Checkerboard", "Red Refractor") — this avoids
    // false positives from team names/colors (Blue Jays, Reds, Astros-orange, etc.)
    private static readonly string[] ColorWords = new[]
    {
        "blue", "red", "green", "purple", "orange", "pink", "yellow", "black", "silver", "bronze"
    };

    private static readonly string[] ParallelBaseTerms = new[]
    {
        "checkerboard", "refractor", "wave", "shimmer", "prizm", "chrome"
    };

    public static ParsedTitle Parse(string ebayTitle)
    {
        var result = new ParsedTitle();
        var working = ebayTitle;

        var gradeMatch = Regex.Match(working, @"\b(PSA|BGS|SGC|CGC)\s*(\d+(\.\d)?)\b", RegexOptions.IgnoreCase);
        if (gradeMatch.Success)
        {
            result.Grade = (gradeMatch.Groups[1].Value.ToUpper(), decimal.Parse(gradeMatch.Groups[2].Value));
        }
        working = Regex.Replace(working, @"\b(PSA|BGS|SGC|CGC|TAG)\s*\d+(\.\d)?\b", "", RegexOptions.IgnoreCase);

        var cardNumMatch = Regex.Match(working, @"#([A-Z]{0,4}-?\d+[A-Z]?)", RegexOptions.IgnoreCase);
        if (cardNumMatch.Success)
        {
            result.CardNumber = cardNumMatch.Groups[1].Value.ToUpper();
        }

        var lowerWorking = working.ToLower();

        // Detect color-adjacent-to-parallel-base pattern, e.g. "blue checkerboard", "red refractor"
        foreach (var color in ColorWords)
        {
            foreach (var baseTerm in ParallelBaseTerms)
            {
                if (Regex.IsMatch(lowerWorking, $@"\b{color}\s+{baseTerm}\b") ||
                    Regex.IsMatch(lowerWorking, $@"\b{baseTerm}\s+{color}\b"))
                {
                    result.ColorModifier = color;
                }
            }
        }

        foreach (var parallel in KnownParallels)
        {
            if (Regex.IsMatch(lowerWorking, $@"\b{Regex.Escape(parallel)}\b"))
                result.ParallelKeywords.Add(parallel);
        }

        working = Regex.Replace(working, @"\b(RC|MINT|NM|RARE|HOT|MUST SEE|L@@K|FREE SHIPPING)\b", "", RegexOptions.IgnoreCase);
        working = Regex.Replace(working, @"[*]", " ");
        working = Regex.Replace(working, @"\s+", " ").Trim();

        result.CleanedForSearch = working;
        return result;
    }
}