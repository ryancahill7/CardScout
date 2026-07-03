namespace CardScout.Core.Matching;

public static class MatchScorer
{
    public static double ScoreMatch(ParsedTitle ebayParsed, string candidateProductName, string candidateConsoleName)
    {
        var candidateFull = $"{candidateConsoleName} {candidateProductName}".ToLower();
        var ebayLower = ebayParsed.CleanedForSearch.ToLower();

        var ebayTokens = ebayLower.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var candidateTokens = candidateFull.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        double baseScore = 0;
        if (ebayTokens.Count > 0 && candidateTokens.Count > 0)
        {
            var overlap = ebayTokens.Intersect(candidateTokens).Count();
            var union = ebayTokens.Union(candidateTokens).Count();
            baseScore = union == 0 ? 0 : (double)overlap / union;
        }

        if (!string.IsNullOrEmpty(ebayParsed.CardNumber))
        {
            var candidateHasNumber = candidateProductName.Contains('#');
            var candidateNumber = candidateHasNumber
                ? System.Text.RegularExpressions.Regex.Match(candidateProductName, @"#([A-Z]{0,4}-?\d+[A-Z]?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value.ToUpper()
                : "";

            if (candidateNumber == ebayParsed.CardNumber)
                baseScore += 0.25;
            else if (candidateHasNumber && candidateNumber != ebayParsed.CardNumber)
                baseScore -= 0.35;
        }

        var candidateLower = candidateProductName.ToLower();

        // Strongest signal: if eBay title has a specific color modifier, candidate MUST match it exactly.
        // A color mismatch here means a different, differently-priced parallel — treat as a hard miss.
        if (!string.IsNullOrEmpty(ebayParsed.ColorModifier))
        {
            if (candidateLower.Contains(ebayParsed.ColorModifier))
                baseScore += 0.20;
            else if (candidateLower.Contains('[') && candidateLower.Contains(']'))
                baseScore -= 0.50; // candidate has SOME color/parallel bracket but not the right one
        }
        else
        {
            // eBay title has no specific color — heavily penalize candidates that DO have a
            // bracketed color/parallel modifier, since that's a different, likely mispriced variant.
            var candidateHasAnyParallelMarker = candidateLower.Contains('[') && candidateLower.Contains(']');
            if (candidateHasAnyParallelMarker)
                baseScore -= 0.30; // raised from 0.10 — this was too weak to prevent bad matches
        }

        if (ebayParsed.ParallelKeywords.Any())
        {
            var matchedParallel = ebayParsed.ParallelKeywords.Any(p => candidateLower.Contains(p));
            if (!matchedParallel)
                baseScore -= 0.15;
        }

        return Math.Clamp(baseScore, 0, 1);
    }
}