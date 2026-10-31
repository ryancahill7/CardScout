using CardScout.Core.Comps;
using CardScout.Core.Data;
using CardScout.Core.Matching;
using CardScout.Core.Models;

namespace CardScout.Core.Flip;

public record CompMatch(CompCacheRecord Comp, double Confidence, ParsedTitle Parsed);

// Match is null when the title couldn't be matched; NoMatchReason says why (for logs)
public record CompMatchResult(CompMatch? Match, string? NoMatchReason);

// Matches an eBay listing title to a SportsCardsPro product: TitleCleaner -> COMP_CACHE
// (by cleaned query) or CompsClient search -> MatchScorer. Shared by MatcherService and
// the WatchlistWorker so both score listings the same way.
public class ListingEvaluator
{
    public const double MinConfidence = 0.55;

    private readonly CompCacheRepository _compCacheRepo;
    private readonly CompsClient _compsClient;

    public ListingEvaluator(CompCacheRepository compCacheRepo, CompsClient compsClient)
    {
        _compCacheRepo = compCacheRepo;
        _compsClient = compsClient;
    }

    // category "tcg" matches the title as a Pokemon/Magic/Yu-Gi-Oh!/... card (game is a hint
    // when the title doesn't name it); anything else uses the sports pipeline
    public async Task<CompMatchResult> MatchAsync(string title, string? category = null, string? game = null)
    {
        if (TcgMatcher.IsTcg(category)) return await MatchTcgAsync(title, game);

        var parsed = TitleCleaner.Parse(title);
        if (string.IsNullOrWhiteSpace(parsed.CleanedForSearch))
            return new CompMatchResult(null, "empty title");

        var cached = await _compCacheRepo.GetCachedAsync(parsed.CleanedForSearch);
        if (cached is { HasFullLadder: false, ProductId: not null } && parsed.Grade is { Grade: < 7 })
        {
            // Cached before grades 1-6 were stored; a low-grade slab needs them, so refresh once
            var refreshed = await _compsClient.GetProductAsync(cached.ProductId);
            if (refreshed != null)
            {
                cached = CompCacheRecord.FromDetail(parsed.CleanedForSearch, refreshed);
                await _compCacheRepo.UpsertAsync(cached);
            }
        }
        if (cached != null)
        {
            var cachedScore = MatchScorer.ScoreMatch(parsed, cached.ProductName ?? "", cached.ConsoleName ?? "");
            return cachedScore < MinConfidence
                ? new CompMatchResult(null, $"low confidence {cachedScore:F2} (cached)")
                : new CompMatchResult(new CompMatch(cached, cachedScore, parsed), null);
        }

        var candidates = await _compsClient.SearchAsync(parsed.CleanedForSearch);
        if (candidates.Count == 0)
            return new CompMatchResult(null, "no candidates");

        var best = candidates
            .Select(c => new { Candidate = c, Score = MatchScorer.ScoreMatch(parsed, c.ProductName, c.ConsoleName) })
            .OrderByDescending(x => x.Score)
            .First();

        if (best.Score < MinConfidence)
            return new CompMatchResult(null, $"low confidence {best.Score:F2} -> best candidate: {best.Candidate.ProductName}");

        var detail = await _compsClient.GetProductAsync(best.Candidate.Id);
        if (detail == null)
            return new CompMatchResult(null, "product lookup returned nothing");

        var record = CompCacheRecord.FromDetail(parsed.CleanedForSearch, detail);
        await _compCacheRepo.UpsertAsync(record);
        return new CompMatchResult(new CompMatch(record, best.Score, parsed), null);
    }

    private async Task<CompMatchResult> MatchTcgAsync(string title, string? game)
    {
        var query = TcgMatcher.ParseTitle(title, game);
        var search = TcgMatcher.SearchQueries(query).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(search))
            return new CompMatchResult(null, "empty title");

        // Callers only read Grade and CleanedForSearch from the parsed title
        var parsed = new ParsedTitle { CleanedForSearch = search, CardNumber = query.Number, Grade = query.Grade };
        var cacheKey = $"tcg|{search}";

        var cached = await _compCacheRepo.GetCachedAsync(cacheKey);
        if (cached != null)
        {
            var cachedScore = TcgMatcher.Score(query, cached.ProductName ?? "", cached.ConsoleName ?? "");
            return cachedScore < MinConfidence
                ? new CompMatchResult(null, $"low confidence {cachedScore:F2} (cached)")
                : new CompMatchResult(new CompMatch(cached, cachedScore, parsed), null);
        }

        var candidates = await _compsClient.SearchAsync(search, tradingCardGames: true);
        if (candidates.Count == 0)
            return new CompMatchResult(null, "no candidates");

        var best = candidates
            .Select(c => new { Candidate = c, Score = TcgMatcher.Score(query, c.ProductName, c.ConsoleName) })
            .OrderByDescending(x => x.Score)
            .First();

        if (best.Score < MinConfidence)
            return new CompMatchResult(null, $"low confidence {best.Score:F2} -> best candidate: {best.Candidate.ProductName}");

        var detail = await _compsClient.GetProductAsync(best.Candidate.Id);
        if (detail == null)
            return new CompMatchResult(null, "product lookup returned nothing");

        var record = CompCacheRecord.FromDetail(cacheKey, detail);
        await _compCacheRepo.UpsertAsync(record);
        return new CompMatchResult(new CompMatch(record, best.Score, parsed), null);
    }
}
