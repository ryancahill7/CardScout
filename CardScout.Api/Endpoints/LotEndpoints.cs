using Anthropic.Exceptions;
using CardScout.Core.Comps;
using CardScout.Core.Flip;
using CardScout.Core.Identify;
using CardScout.Core.Images;
using CardScout.Core.Models;
using CardScout.Core.Pricing;

namespace CardScout.Api.Endpoints;

public static class LotEndpoints
{
    // Each card costs ~2 SportsCardsPro calls at ~1/sec, so a big lot takes a while
    private const int MaxCardsToPrice = 20;

    public static void MapLotEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/lots/analyze", AnalyzeAsync).DisableAntiforgery();
    }

    // Multipart: photo (required), askingPrice, shipping, minProfit, minProfitPct (optional form fields)
    private static async Task<IResult> AnalyzeAsync(
        HttpRequest request, CurrentUser user, CardIdentifier identifier, CardPricingService pricing, IImageStorage images,
        ILogger<CardIdentifier> logger, CancellationToken ct)
    {
        if (!request.HasFormContentType) return Results.BadRequest(new { error = "Send the lot photo as multipart form data." });
        var form = await request.ReadFormAsync(ct);
        var photo = form.Files.GetFile("photo");

        var error = CardEndpoints.ValidateImage(photo, "photo");
        if (error != null) return Results.BadRequest(new { error });

        decimal? Number(string key) =>
            decimal.TryParse(form[key].ToString(), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

        var asking = Number("askingPrice");
        var shipping = Number("shipping") ?? 0;
        var targets = new FlipTargets(Number("minProfit") ?? FlipTargets.Default.MinProfit,
            Number("minProfitPct") ?? FlipTargets.Default.MinProfitPct);

        var image = await CardEndpoints.ReadImageAsync(photo!, ct);

        LotIdentifyResult lot;
        try
        {
            lot = await identifier.IdentifyLotAsync(image, ct);
        }
        catch (CardIdentificationException ex)
        {
            logger.LogWarning(ex, "Lot identification failed. Raw response: {Raw}", ex.RawResponse);
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
        }
        catch (AnthropicApiException ex)
        {
            logger.LogError(ex, "Anthropic API call failed");
            return Results.Json(new { error = "The identification service returned an error. Try again in a moment." },
                statusCode: StatusCodes.Status502BadGateway);
        }

        if (lot.Usage != null)
            logger.LogInformation("Lot scan by user {UserId}: {Model} {InputTokens} in / {OutputTokens} out tokens, {Cards} cards",
                user.UserId, lot.Usage.Model, lot.Usage.InputTokens, lot.Usage.OutputTokens, lot.Cards.Count);

        var results = new List<LotCardResult>();
        foreach (var card in lot.Cards.Take(MaxCardsToPrice))
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var quote = await pricing.QuoteAsync(card.ToIdentification());
                results.Add(new LotCardResult(card, quote, quote.NetProceeds, null));
            }
            catch (Exception ex) when (ex is CompsApiException or HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(ex, "Pricing failed for lot card {Player}", card.Player);
                results.Add(new LotCardResult(card, CardPriceQuote.None, null,
                    ex is CompsApiException ? ex.Message : "Couldn't get a price from SportsCardsPro."));
            }
        }
        // Cards past the cap are still listed, just unpriced
        results.AddRange(lot.Cards.Skip(MaxCardsToPrice)
            .Select(c => new LotCardResult(c, CardPriceQuote.None, null, $"Only the first {MaxCardsToPrice} cards are priced.")));

        var prices = results.Where(r => r.Price.SuggestedPrice != null).Select(r => r.Price.SuggestedPrice!.Value).ToList();
        var flip = asking is > 0 ? FlipAnalyzer.AnalyzeLot(prices, asking.Value, shipping, targets) : null;

        var photoPath = await images.SaveAsync(new MemoryStream(image.Data), CardEndpoints.AllowedImageTypes[image.MediaType], user.ImageFolder);

        return Results.Ok(new LotAnalysisResponse(results, lot.Notes, prices.Sum(), prices.Count, flip, photoPath));
    }
}
