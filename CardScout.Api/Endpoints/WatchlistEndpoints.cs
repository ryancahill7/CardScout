using CardScout.Core.Data;
using CardScout.Core.Models;

namespace CardScout.Api.Endpoints;

public static class WatchlistEndpoints
{
    public static void MapWatchlistEndpoints(this IEndpointRouteBuilder app)
    {
        var lists = app.MapGroup("/api/watchlists");

        lists.MapGet("/", ListAsync);
        lists.MapPost("/", CreateAsync);
        lists.MapPut("/{id:long}", UpdateAsync);
        lists.MapDelete("/{id:long}", DeleteAsync);

        lists.MapGet("/hits", HitsAsync);
        lists.MapGet("/hits/{id:long}", GetHitAsync);
        lists.MapPatch("/hits/{id:long}", UpdateHitAsync);
    }

    private static async Task<IResult> ListAsync(CurrentUser user, WatchlistRepository repo) =>
        Results.Ok((await repo.GetAllAsync(user.UserId)).Select(WatchlistDto.From));

    private static async Task<IResult> CreateAsync(WatchlistRequest request, CurrentUser user, WatchlistRepository repo)
    {
        var (watchlist, error) = FromRequest(request, user.UserId);
        if (error != null) return Results.BadRequest(new { error });

        var id = await repo.InsertAsync(watchlist!);
        var saved = await repo.GetByIdAsync(user.UserId, id);
        return Results.Created($"/api/watchlists/{id}", WatchlistDto.From(saved!));
    }

    private static async Task<IResult> UpdateAsync(long id, WatchlistRequest request, CurrentUser user, WatchlistRepository repo)
    {
        if (await repo.GetByIdAsync(user.UserId, id) == null) return Results.NotFound();

        var (watchlist, error) = FromRequest(request, user.UserId);
        if (error != null) return Results.BadRequest(new { error });

        watchlist!.Id = id;
        await repo.UpdateAsync(watchlist);
        return Results.Ok(WatchlistDto.From((await repo.GetByIdAsync(user.UserId, id))!));
    }

    private static async Task<IResult> DeleteAsync(long id, CurrentUser user, WatchlistRepository repo) =>
        await repo.DeleteAsync(user.UserId, id) ? Results.NoContent() : Results.NotFound();

    // ?status=SAVED to filter, ?all=true to include unprofitable listings, ?watchlistId= for one list
    private static async Task<IResult> HitsAsync(string? status, bool? all, long? watchlistId, CurrentUser user, WatchlistRepository repo)
    {
        string? normalized = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            normalized = status.Trim().ToUpperInvariant();
            if (!WatchlistHitStatus.All.Contains(normalized))
                return Results.BadRequest(new { error = $"status must be one of {string.Join(", ", WatchlistHitStatus.All)}." });
        }

        var hits = await repo.GetHitsAsync(user.UserId, normalized, profitableOnly: all != true, watchlistId);
        return Results.Ok(hits.Select(WatchlistHitDto.From));
    }

    private static async Task<IResult> GetHitAsync(long id, CurrentUser user, WatchlistRepository repo)
    {
        var hit = await repo.GetHitAsync(user.UserId, id);
        return hit == null ? Results.NotFound() : Results.Ok(WatchlistHitDto.From(hit));
    }

    private static async Task<IResult> UpdateHitAsync(long id, HitStatusRequest request, CurrentUser user, WatchlistRepository repo)
    {
        var status = request.Status?.Trim().ToUpperInvariant() ?? "";
        if (!WatchlistHitStatus.All.Contains(status))
            return Results.BadRequest(new { error = $"status must be one of {string.Join(", ", WatchlistHitStatus.All)}." });

        if (!await repo.UpdateHitStatusAsync(user.UserId, id, status)) return Results.NotFound();
        return Results.Ok(WatchlistHitDto.From((await repo.GetHitAsync(user.UserId, id))!));
    }

    private static (Watchlist?, string?) FromRequest(WatchlistRequest r, long userId)
    {
        var name = r.Name?.Trim() ?? "";
        var query = r.Query?.Trim() ?? "";
        if (query.Length < 3 || query.Length > 200) return (null, "query must be 3-200 characters.");
        if (name.Length == 0) name = query;
        if (name.Length > 100) return (null, "name must be 100 characters or fewer.");

        var type = (r.ListingType ?? WatchlistListingType.Any).Trim().ToUpperInvariant();
        if (!WatchlistListingType.All.Contains(type))
            return (null, $"listingType must be one of {string.Join(", ", WatchlistListingType.All)}.");

        if (r.MaxPrice is <= 0) return (null, "maxPrice must be more than 0.");
        if (r.MinProfit is < 0) return (null, "minProfit can't be negative.");
        if (r.MinProfitPct is < 0 or > 1000) return (null, "minProfitPct must be between 0 and 1000.");

        return (new Watchlist
        {
            UserId = userId,
            Name = name,
            Query = query,
            ListingType = type,
            MaxPrice = r.MaxPrice,
            MinProfit = r.MinProfit ?? 5m,
            MinProfitPct = r.MinProfitPct ?? 20m,
            GradedOnly = r.GradedOnly ? "Y" : "N",
            Notify = r.Notify ? "Y" : "N",
            IsActive = r.IsActive ? "Y" : "N"
        }, null);
    }
}
