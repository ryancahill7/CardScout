namespace CardScout.Core.Matching;

public static class MatchScorer
{
    public static double ScoreMatch(ParsedTitle ebayParsed, string candidateProductName, string candidateConsoleName)
    {
        var candidateFull = $"{candidateConsoleName} {candidateProductName}".ToLower();
        var ebayLower = ebayParsed.CleanedForSearch.ToLower();

        // Base score: token overlap (same as before)
        var ebayTokens = ebayLower.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var candidateTokens = candidateFull.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        double baseScore = 0;
        if (ebayTokens.Count > 0 && candidateTokens.Count > 0)
        {
            var overlap = ebayTokens.Intersect(candidateTokens).Count();
            var union = ebayTokens.Union(candidateTokens).Count();
            baseScore = union == 0 ? 0 : (double)overlap / union;
        }

        // Card number: strong positive signal if it matches, strong NEGATIVE if both have numbers and they differ
        if (!string.IsNullOrEmpty(ebayParsed.CardNumber))
        {
            var candidateHasNumber = candidateProductName.Contains('#');
            var candidateNumber = candidateHasNumber
                ? System.Text.RegularExpressions.Regex.Match(candidateProductName, @"#([A-Z]{0,4}-?\d+[A-Z]?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value.ToUpper()
                : "";

            if (candidateNumber == ebayParsed.CardNumber)
                baseScore += 0.25; // strong boost — this is the single best disambiguator
            else if (candidateHasNumber && candidateNumber != ebayParsed.CardNumber)
                baseScore -= 0.35; // strong penalty — different card number means different card, full stop
        }

        // Parallel keywords: penalize if eBay title mentions a parallel the candidate doesn't have, or vice versa
        var candidateLower = candidateProductName.ToLower();
        var candidateHasAnyParallelMarker = candidateLower.Contains('[') && candidateLower.Contains(']');

        if (ebayParsed.ParallelKeywords.Any())
        {
            var matchedParallel = ebayParsed.ParallelKeywords.Any(p => candidateLower.Contains(p));
            if (!matchedParallel)
                baseScore -= 0.15; // eBay title calls out a parallel this candidate doesn't have
        }
        else if (candidateHasAnyParallelMarker)
        {
            baseScore -= 0.10; // candidate IS a parallel but eBay title didn't mention one — likely base card being compared to a parallel
        }

        return Math.Clamp(baseScore, 0, 1);
    }
}