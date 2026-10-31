using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Exceptions;
using CardScout.Core.Comps;
using CardScout.Core.Data;
using CardScout.Core.Flip;
using CardScout.Core.Identify;
using CardScout.Core.Images;
using CardScout.Core.Listing;
using CardScout.Core.Models;
using CardScout.Core.Pricing;

namespace CardScout.Api.Endpoints;

public static class CardEndpoints
{
    // Image types the Anthropic API accepts
    internal static readonly Dictionary<string, string> AllowedImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif"
    };

    private const long MaxImageBytes = 10 * 1024 * 1024;

    public static void MapCardEndpoints(this IEndpointRouteBuilder app)
    {
        var cards = app.MapGroup("/api/cards");

        cards.MapPost("/identify", IdentifyAsync).DisableAntiforgery();
        cards.MapPost("/price", PriceAsync);
        cards.MapPost("/", CreateAsync);
        cards.MapGet("/", ListAsync);
        cards.MapGet("/{id:long}", GetAsync);
        cards.MapPatch("/{id:long}", PatchAsync);
        cards.MapGet("/{id:long}/images/{side}", GetImageAsync);
        cards.MapPost("/market", MarketAsync);
        cards.MapGet("/{id:long}/listing-kit", ListingKitAsync);
        cards.MapGet("/{id:long}/history", HistoryAsync);
    }

    private static async Task<IResult> IdentifyAsync(
        IFormFile? front, IFormFile? back, CurrentUser user,
        CardIdentifier identifier, CardPricingService pricing, IImageStorage images,
        ILogger<CardIdentifier> logger, CancellationToken ct)
    {
        // The back is optional: show mode scans the front only, for speed
        var error = ValidateImage(front, "front") ?? (back == null ? null : ValidateImage(back, "back"));
        if (error != null) return Results.BadRequest(new { error });

        var frontImage = await ReadImageAsync(front!, ct);
        var backImage = back == null ? null : await ReadImageAsync(back, ct);

        CardIdentifyResult result;
        try
        {
            result = await identifier.IdentifyAsync(frontImage, backImage, ct);
        }
        catch (CardIdentificationException ex)
        {
            logger.LogWarning(ex, "Card identification failed. Raw response: {Raw}", ex.RawResponse);
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
        }
        catch (AnthropicApiException ex)
        {
            logger.LogError(ex, "Anthropic API call failed");
            return Results.Json(new { error = "The identification service returned an error. Try again in a moment." },
                statusCode: StatusCodes.Status502BadGateway);
        }

        if (result.Usage != null)
            logger.LogInformation("Scan by user {UserId}: {Model} {InputTokens} in / {OutputTokens} out tokens ({Sides})",
                user.UserId, result.Usage.Model, result.Usage.InputTokens, result.Usage.OutputTokens, back == null ? "front only" : "front+back");

        // Pricing is best-effort: a SportsCardsPro outage shouldn't lose the identification
        var price = CardPriceQuote.None;
        string? priceError = null;
        try
        {
            price = await pricing.QuoteAsync(result.Card);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Price suggestion failed for {Title}", result.Card.SuggestedEbayTitle);
            priceError = ex is CompsApiException ? ex.Message : "Couldn't get a price from SportsCardsPro.";
        }

        // Saved now so the client doesn't have to re-upload on save. Images from scans
        // that are never saved stay on disk; clean them up if that becomes a problem.
        var frontPath = await images.SaveAsync(new MemoryStream(frontImage.Data), AllowedImageTypes[frontImage.MediaType], user.ImageFolder);
        var backPath = backImage == null
            ? null
            : await images.SaveAsync(new MemoryStream(backImage.Data), AllowedImageTypes[backImage.MediaType], user.ImageFolder);

        return Results.Ok(new IdentifyResponse(result.Card, price, priceError, frontPath, backPath, result.RawJson));
    }

    // Re-prices a card from (possibly edited) fields; used after the user corrects the
    // identification, picks a different comp, or refreshes an inventory card.
    private static async Task<IResult> PriceAsync(
        PriceQuoteRequest request, CardPricingService pricing, ILogger<CardPricingService> logger)
    {
        try
        {
            return Results.Ok(await pricing.QuoteAsync(request.ToIdentification(), request.CompProductId));
        }
        catch (CompsApiException ex)
        {
            logger.LogWarning(ex, "Price quote failed for {Player} {Year} {Set}", request.Player, request.Year, request.Set);
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Price quote failed for {Player} {Year} {Set}", request.Player, request.Year, request.Set);
            return Results.Json(new { error = "SportsCardsPro didn't respond. Try again in a moment." },
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> CreateAsync(CreateCardRequest request, CurrentUser user, CardInventoryRepository repo, IImageStorage images)
    {
        var title = request.EbayTitle?.Trim() ?? "";
        if (title.Length == 0 || title.Length > 80)
            return Results.BadRequest(new { error = "ebayTitle is required and must be 80 characters or fewer." });

        var status = NormalizeStatus(request.Status ?? CardStatus.Unlisted);
        if (status == null)
            return Results.BadRequest(new { error = $"status must be one of {string.Join(", ", CardStatus.All)}." });

        foreach (var path in new[] { request.FrontImagePath, request.BackImagePath })
        {
            if (path == null) continue;
            // Clients can only attach images that were uploaded under their own account
            if (!path.StartsWith(user.ImageFolder + "/", StringComparison.Ordinal))
                return Results.BadRequest(new { error = $"Image '{path}' was not found." });
            await using var stream = await images.OpenReadAsync(path);
            if (stream == null) return Results.BadRequest(new { error = $"Image '{path}' was not found." });
        }

        var item = new CardInventoryItem
        {
            UserId = user.UserId,
            Player = request.Player,
            CardYear = request.Year,
            Manufacturer = request.Manufacturer,
            SetName = request.Set,
            CardNumber = request.CardNumber,
            Parallel = request.Parallel,
            SerialNumber = request.SerialNumber,
            IsRookie = YN(request.IsRookie),
            IsAutograph = YN(request.IsAutograph),
            IsRelic = YN(request.IsRelic),
            GradingCompany = request.GradingCompany,
            Grade = request.Grade,
            ConditionNotes = request.ConditionNotes,
            AiConfidence = request.Confidence,
            EbayTitle = title,
            SuggestedPrice = request.SuggestedPrice,
            CompProductId = request.CompProductId,
            CostBasis = request.CostBasis,
            Status = status,
            SoldAt = status == CardStatus.Sold ? DateTime.Now : null,
            FrontImagePath = request.FrontImagePath,
            BackImagePath = request.BackImagePath,
            AiRawJson = request.AiRawJson,
            PurchaseSource = request.PurchaseSource?.Trim(),
            PurchasedAt = request.PurchasedAt,
            GradingCost = request.GradingCost,
            CertNumber = request.CertNumber?.Trim()
        };

        var id = await repo.InsertAsync(item);
        var saved = await repo.GetByIdAsync(user.UserId, id);
        return Results.Created($"/api/cards/{id}", CardDto.From(saved!));
    }

    private static async Task<IResult> ListAsync(string? status, CurrentUser user, CardInventoryRepository repo)
    {
        string? normalized = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            normalized = NormalizeStatus(status);
            if (normalized == null)
                return Results.BadRequest(new { error = $"status must be one of {string.Join(", ", CardStatus.All)}." });
        }

        var items = await repo.GetAllAsync(user.UserId, normalized);
        return Results.Ok(items.Select(CardDto.From));
    }

    private static async Task<IResult> GetAsync(long id, CurrentUser user, CardInventoryRepository repo)
    {
        var item = await repo.GetByIdAsync(user.UserId, id);
        return item == null ? Results.NotFound() : Results.Ok(CardDto.From(item));
    }

    // Only the properties present in the body are changed; send null to clear a field.
    private static async Task<IResult> PatchAsync(long id, JsonObject body, CurrentUser user, CardInventoryRepository repo)
    {
        var item = await repo.GetByIdAsync(user.UserId, id);
        if (item == null) return Results.NotFound();

        try
        {
            foreach (var (key, value) in body)
            {
                switch (key)
                {
                    case "player": item.Player = Str(value); break;
                    case "year": item.CardYear = value?.GetValue<int?>(); break;
                    case "manufacturer": item.Manufacturer = Str(value); break;
                    case "set": item.SetName = Str(value); break;
                    case "cardNumber": item.CardNumber = Str(value); break;
                    case "parallel": item.Parallel = Str(value); break;
                    case "serialNumber": item.SerialNumber = Str(value); break;
                    case "isRookie": item.IsRookie = YN(value?.GetValue<bool>() ?? false); break;
                    case "isAutograph": item.IsAutograph = YN(value?.GetValue<bool>() ?? false); break;
                    case "isRelic": item.IsRelic = YN(value?.GetValue<bool>() ?? false); break;
                    case "gradingCompany": item.GradingCompany = Str(value); break;
                    case "grade": item.Grade = value?.GetValue<decimal?>(); break;
                    case "conditionNotes": item.ConditionNotes = Str(value); break;
                    case "suggestedPrice": item.SuggestedPrice = value?.GetValue<decimal?>(); break;
                    case "compProductId": item.CompProductId = Str(value); break;
                    case "costBasis": item.CostBasis = value?.GetValue<decimal?>(); break;
                    case "listedPrice": item.ListedPrice = value?.GetValue<decimal?>(); break;
                    case "soldPrice": item.SoldPrice = value?.GetValue<decimal?>(); break;
                    case "soldAt": item.SoldAt = value?.GetValue<DateTime?>(); break;
                    case "purchaseSource": item.PurchaseSource = Str(value); break;
                    case "purchasedAt": item.PurchasedAt = value?.GetValue<DateTime?>(); break;
                    case "certNumber": item.CertNumber = Str(value); break;
                    case "gradingCost": item.GradingCost = value?.GetValue<decimal?>(); break;
                    case "saleShippingCost": item.SaleShippingCost = value?.GetValue<decimal?>(); break;
                    case "ebayTitle":
                        var title = Str(value)?.Trim() ?? "";
                        if (title.Length == 0 || title.Length > 80)
                            return Results.BadRequest(new { error = "ebayTitle must be 1-80 characters." });
                        item.EbayTitle = title;
                        break;
                    case "status":
                        var status = NormalizeStatus(Str(value) ?? "");
                        if (status == null)
                            return Results.BadRequest(new { error = $"status must be one of {string.Join(", ", CardStatus.All)}." });
                        if (status == CardStatus.Sold && item.Status != CardStatus.Sold && !body.ContainsKey("soldAt"))
                            item.SoldAt = DateTime.Now;
                        item.Status = status;
                        break;
                    default:
                        return Results.BadRequest(new { error = $"'{key}' is not an editable field." });
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or JsonException)
        {
            return Results.BadRequest(new { error = $"Invalid value in request: {ex.Message}" });
        }

        await repo.UpdateAsync(item);
        var updated = await repo.GetByIdAsync(user.UserId, id);
        return Results.Ok(CardDto.From(updated!));
    }

    private static async Task<IResult> GetImageAsync(long id, string side, CurrentUser user, CardInventoryRepository repo, IImageStorage images)
    {
        var item = await repo.GetByIdAsync(user.UserId, id);
        if (item == null) return Results.NotFound();

        var path = side.ToLowerInvariant() switch
        {
            "front" => item.FrontImagePath,
            "back" => item.BackImagePath,
            _ => null
        };
        if (path == null) return Results.NotFound();

        var stream = await images.OpenReadAsync(path);
        if (stream == null) return Results.NotFound();

        var contentType = AllowedImageTypes.FirstOrDefault(kv => path.EndsWith(kv.Value, StringComparison.OrdinalIgnoreCase)).Key
                          ?? "application/octet-stream";
        return Results.Stream(stream, contentType);
    }

    // What the card is listed for on eBay right now (asking prices, not sales)
    private static async Task<IResult> MarketAsync(
        PriceQuoteRequest request, MarketSnapshotService market, ILogger<MarketSnapshotService> logger)
    {
        try
        {
            return Results.Ok(await market.GetAsync(request.ToIdentification()));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "eBay market lookup failed for {Player} {Year} {Set}", request.Player, request.Year, request.Set);
            return Results.Json(new { error = "eBay didn't respond. Try again in a moment." }, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    // Title, item specifics, description and a price strategy for listing the card on eBay
    private static async Task<IResult> ListingKitAsync(long id, CurrentUser user, CardInventoryRepository repo)
    {
        var item = await repo.GetByIdAsync(user.UserId, id);
        if (item == null) return Results.NotFound();
        return Results.Ok(ListingKitBuilder.Build(item, item.MarketPrice ?? item.SuggestedPrice));
    }

    private static async Task<IResult> HistoryAsync(long id, int? days, CurrentUser user, CardInventoryRepository repo, PriceHistoryRepository history)
    {
        var item = await repo.GetByIdAsync(user.UserId, id);
        if (item == null) return Results.NotFound();
        var points = await history.GetForCardAsync(user.UserId, id, Math.Clamp(days ?? 180, 1, 730));
        return Results.Ok(points.Select(p => new { p.Price, p.CapturedAt }));
    }

    internal static string? ValidateImage(IFormFile? file, string name)
    {
        if (file == null || file.Length == 0) return $"The '{name}' image is required.";
        if (!AllowedImageTypes.ContainsKey(file.ContentType))
            return $"The '{name}' image must be JPEG, PNG, WebP or GIF (got '{file.ContentType}').";
        if (file.Length > MaxImageBytes) return $"The '{name}' image is larger than 10 MB.";
        return null;
    }

    internal static async Task<CardImage> ReadImageAsync(IFormFile file, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return new CardImage(ms.ToArray(), file.ContentType.ToLowerInvariant());
    }

    private static string? NormalizeStatus(string status)
    {
        var upper = status.Trim().ToUpperInvariant();
        return CardStatus.All.Contains(upper) ? upper : null;
    }

    private static string? Str(JsonNode? value)
    {
        var s = value?.GetValue<string>();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static string YN(bool value) => value ? "Y" : "N";
}
