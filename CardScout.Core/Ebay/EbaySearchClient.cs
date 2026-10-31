using System.Net.Http.Headers;
using System.Text.Json;

namespace CardScout.Core.Ebay;

public record EbayListing(
    string ItemId, string Title, decimal CurrentPrice,
    DateTime? EndTime, string ItemUrl, string? Condition, string ListingType,
    decimal? ShippingCost, string? ImageUrl = null);

public class EbaySearchClient
{
    private readonly HttpClient _http;
    private readonly EbayAuthClient _auth;
    private const string BaseballCardsCategoryId = "212";
    // eBay "Sports Trading Cards" (212) and "Collectible Card Games" (2536, which holds the
    // Pokemon / Magic / Yu-Gi-Oh! / One Piece / Lorcana single-card categories)
    public const string SportsCardsCategoryId = "212";
    public const string TcgCategoryId = "2536";

    public static string CategoryFor(string? category) =>
        Matching.TcgMatcher.IsTcg(category) ? TcgCategoryId : SportsCardsCategoryId;

    public EbaySearchClient(HttpClient http, EbayAuthClient auth)
    {
        _http = http;
        _auth = auth;
    }

    public Task<List<EbayListing>> GetAuctionListingsForPlayerAsync(string playerName, int limit = 20) =>
        SearchAsync(playerName, "AUCTION", limit);

    public Task<List<EbayListing>> GetFixedPriceListingsForPlayerAsync(string playerName, int limit = 20) =>
        SearchAsync(playerName, "FIXED_PRICE", limit);

    private async Task<List<EbayListing>> SearchAsync(string playerName, string buyingOption, int limit)
    {
        var token = await _auth.GetTokenAsync();

        var q = Uri.EscapeDataString(playerName);
        var aspectFilter = Uri.EscapeDataString("categoryId:212,Sport:{Baseball}");

        string filter;
        if (buyingOption == "AUCTION")
        {
            var endBefore = DateTime.UtcNow.AddHours(3).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            filter = Uri.EscapeDataString($"buyingOptions:{{AUCTION}},itemEndDate:[..{endBefore}]");
        }
        else
        {
            filter = Uri.EscapeDataString("buyingOptions:{FIXED_PRICE}");
        }

        var sort = buyingOption == "AUCTION" ? "&sort=endingSoonest" : "&sort=newlyListed";

        var url = "https://api.ebay.com/buy/browse/v1/item_summary/search" +
                   $"?q={q}" +
                   $"&category_ids={BaseballCardsCategoryId}" +
                   $"&aspect_filter={aspectFilter}" +
                   $"&filter={filter}" +
                   sort +
                   $"&limit={limit}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-EBAY-C-MARKETPLACE-ID", "EBAY_US");
        request.Headers.Add("X-EBAY-C-ENDUSERCTX", "contextualLocation=country=US,zip=77079"); // swap in your real Houston zip


        var response = await _http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"eBay API error {response.StatusCode}: {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var results = new List<EbayListing>();
        if (!doc.RootElement.TryGetProperty("itemSummaries", out var items))
            return results;

        foreach (var item in items.EnumerateArray())
        {
            try
            {
                var itemId = item.GetProperty("itemId").GetString()!;
                var title = item.GetProperty("title").GetString()!;

                decimal price = 0;
                var priceField = buyingOption == "AUCTION" ? "currentBidPrice" : "price";
                if (item.TryGetProperty(priceField, out var priceEl) &&
                    priceEl.TryGetProperty("value", out var valueEl))
                {
                    price = decimal.Parse(valueEl.GetString()!);
                }

                var endTime = item.TryGetProperty("itemEndDate", out var end)
                    ? end.GetDateTime()
                    : (DateTime?)null;

                var itemUrl = item.TryGetProperty("itemWebUrl", out var urlEl)
                    ? urlEl.GetString()!
                    : "";

                var condition = item.TryGetProperty("condition", out var condEl)
                    ? condEl.GetString()
                    : null;

                decimal? shippingCost = null;
                if (item.TryGetProperty("shippingOptions", out var shippingArr) &&
                    shippingArr.GetArrayLength() > 0)
                {
                    var firstOption = shippingArr[0];

                    if (firstOption.TryGetProperty("shippingCost", out var scEl) &&
                        scEl.TryGetProperty("value", out var scValEl))
                    {
                        shippingCost = decimal.Parse(scValEl.GetString()!);
                    }
                }

                results.Add(new EbayListing(itemId, title, price, endTime, itemUrl, condition, buyingOption, shippingCost));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Skipped one item due to parse error: {ex.Message}");
            }
        }

        return results;
    }
    public async Task<List<EbayListing>> GetBroadSweepAsync(int limit = 50)
    {
        var token = await _auth.GetTokenAsync();

        var aspectFilter = Uri.EscapeDataString("categoryId:212,Sport:{Baseball}");

        var url = "https://api.ebay.com/buy/browse/v1/item_summary/search" +
                   $"?q=baseball+card" +
                   $"&category_ids={BaseballCardsCategoryId}" +
                   $"&aspect_filter={aspectFilter}" +
                   "&sort=newlyListed" +
                   $"&limit={limit}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-EBAY-C-MARKETPLACE-ID", "EBAY_US");
        request.Headers.Add("X-EBAY-C-ENDUSERCTX", "contextualLocation=country=US,zip=77002");

        var response = await _http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"eBay API error {response.StatusCode}: {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var results = new List<EbayListing>();
        if (!doc.RootElement.TryGetProperty("itemSummaries", out var items))
            return results;

        foreach (var item in items.EnumerateArray())
        {
            try
            {
                var itemId = item.GetProperty("itemId").GetString()!;
                var title = item.GetProperty("title").GetString()!;

                decimal price = 0;
                if (item.TryGetProperty("price", out var priceEl) && priceEl.TryGetProperty("value", out var valueEl))
                    price = decimal.Parse(valueEl.GetString()!);
                else if (item.TryGetProperty("currentBidPrice", out var bidEl) && bidEl.TryGetProperty("value", out var bidValueEl))
                    price = decimal.Parse(bidValueEl.GetString()!);

                var endTime = item.TryGetProperty("itemEndDate", out var end) ? end.GetDateTime() : (DateTime?)null;
                var itemUrl = item.TryGetProperty("itemWebUrl", out var urlEl) ? urlEl.GetString()! : "";
                var condition = item.TryGetProperty("condition", out var condEl) ? condEl.GetString() : null;

                var listingType = "FIXED_PRICE";
                if (item.TryGetProperty("buyingOptions", out var boArr))
                {
                    foreach (var bo in boArr.EnumerateArray())
                    {
                        if (bo.GetString() == "AUCTION") { listingType = "AUCTION"; break; }
                    }
                }

                decimal? shippingCost = null;
                if (item.TryGetProperty("shippingOptions", out var shippingArr) && shippingArr.GetArrayLength() > 0)
                {
                    var firstOption = shippingArr[0];
                    if (firstOption.TryGetProperty("shippingCost", out var scEl) && scEl.TryGetProperty("value", out var scValEl))
                        shippingCost = decimal.Parse(scValEl.GetString()!);
                }

                results.Add(new EbayListing(itemId, title, price, endTime, itemUrl, condition, listingType, shippingCost));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Skipped one item due to parse error: {ex.Message}");
            }
        }

        return results;
    }
    // General keyword search across all sports cards (not just baseball), or trading card
    // games when categoryId is TcgCategoryId; used by watchlists and the market check.
    // buyingOption is AUCTION, FIXED_PRICE or null for both; sort is an eBay sort value like
    // "newlyListed" or "endingSoonest".
    public async Task<List<EbayListing>> SearchListingsAsync(
        string query, string? buyingOption, string sort, int limit = 25, DateTime? endBefore = null, decimal? maxPrice = null,
        string categoryId = SportsCardsCategoryId)
    {
        var token = await _auth.GetTokenAsync();

        var filters = new List<string>();
        if (buyingOption != null) filters.Add($"buyingOptions:{{{buyingOption}}}");
        if (endBefore != null) filters.Add($"itemEndDate:[..{endBefore.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffZ}]");
        if (maxPrice != null) filters.Add($"price:[..{maxPrice.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}],priceCurrency:USD");

        var url = "https://api.ebay.com/buy/browse/v1/item_summary/search" +
                  $"?q={Uri.EscapeDataString(query)}" +
                  $"&category_ids={categoryId}" +
                  (filters.Count > 0 ? $"&filter={Uri.EscapeDataString(string.Join(",", filters))}" : "") +
                  $"&sort={sort}" +
                  $"&limit={Math.Clamp(limit, 1, 200)}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-EBAY-C-MARKETPLACE-ID", "EBAY_US");
        request.Headers.Add("X-EBAY-C-ENDUSERCTX", "contextualLocation=country=US,zip=77002");

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"eBay API error {response.StatusCode}: {errorBody}");
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var results = new List<EbayListing>();
        if (!doc.RootElement.TryGetProperty("itemSummaries", out var items))
            return results;

        foreach (var item in items.EnumerateArray())
        {
            try
            {
                results.Add(ParseSummary(item));
            }
            catch (Exception ex) when (ex is KeyNotFoundException or FormatException or InvalidOperationException)
            {
                Console.WriteLine($"Skipped one item due to parse error: {ex.Message}");
            }
        }
        return results;
    }

    private static EbayListing ParseSummary(JsonElement item)
    {
        static decimal? Money(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var el) && el.TryGetProperty("value", out var v)
                ? decimal.Parse(v.GetString()!, System.Globalization.CultureInfo.InvariantCulture)
                : null;

        var listingType = "FIXED_PRICE";
        if (item.TryGetProperty("buyingOptions", out var boArr) &&
            boArr.EnumerateArray().Any(bo => bo.GetString() == "AUCTION"))
            listingType = "AUCTION";

        // Auctions carry the live bid in currentBidPrice; "price" can be the start price
        var price = (listingType == "AUCTION" ? Money(item, "currentBidPrice") ?? Money(item, "price") : Money(item, "price"))
                    ?? Money(item, "currentBidPrice") ?? 0;

        decimal? shipping = null;
        if (item.TryGetProperty("shippingOptions", out var shippingArr) && shippingArr.GetArrayLength() > 0)
            shipping = Money(shippingArr[0], "shippingCost");

        string? image = null;
        if (item.TryGetProperty("image", out var img) && img.TryGetProperty("imageUrl", out var imgUrl))
            image = imgUrl.GetString();
        else if (item.TryGetProperty("thumbnailImages", out var thumbs) && thumbs.GetArrayLength() > 0 &&
                 thumbs[0].TryGetProperty("imageUrl", out var thumbUrl))
            image = thumbUrl.GetString();

        return new EbayListing(
            item.GetProperty("itemId").GetString()!,
            item.GetProperty("title").GetString()!,
            price,
            item.TryGetProperty("itemEndDate", out var end) ? end.GetDateTime() : null,
            item.TryGetProperty("itemWebUrl", out var urlEl) ? urlEl.GetString() ?? "" : "",
            item.TryGetProperty("condition", out var condEl) ? condEl.GetString() : null,
            listingType,
            shipping,
            image);
    }
    public async Task<decimal?> GetActualShippingCostAsync(string itemId)
    {
        var token = await _auth.GetTokenAsync();
        var url = $"https://api.ebay.com/buy/browse/v1/item/{Uri.EscapeDataString(itemId)}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-EBAY-C-ENDUSERCTX", "contextualLocation=country=US,zip=77002");

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("shippingOptions", out var opts) && opts.GetArrayLength() > 0 &&
            opts[0].TryGetProperty("shippingCost", out var sc) && sc.TryGetProperty("value", out var val))
        {
            return decimal.Parse(val.GetString()!);
        }
        return null;
    }
    public async Task<List<EbayListing>> GetNewestListingsAsync(string playerName, int limit = 20)
    {
        var token = await _auth.GetTokenAsync();

        var q = Uri.EscapeDataString(playerName);
        var aspectFilter = Uri.EscapeDataString("categoryId:212,Sport:{Baseball}");

        // No buyingOptions filter at all — "any kind" means auctions, fixed price, everything
        var url = "https://api.ebay.com/buy/browse/v1/item_summary/search" +
                   $"?q={q}" +
                   $"&category_ids={BaseballCardsCategoryId}" +
                   $"&aspect_filter={aspectFilter}" +
                   "&sort=newlyListed" +
                   $"&limit={limit}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-EBAY-C-MARKETPLACE-ID", "EBAY_US");
        request.Headers.Add("X-EBAY-C-ENDUSERCTX", "contextualLocation=country=US,zip=77002");

        var response = await _http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"eBay API error {response.StatusCode}: {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var results = new List<EbayListing>();
        if (!doc.RootElement.TryGetProperty("itemSummaries", out var items))
            return results;

        foreach (var item in items.EnumerateArray())
        {
            try
            {
                var itemId = item.GetProperty("itemId").GetString()!;
                var title = item.GetProperty("title").GetString()!;

                // Mixed result set (auctions + fixed price) — try fixed "price" first,
                // fall back to auction "currentBidPrice" since only one will be present per item
                decimal price = 0;
                if (item.TryGetProperty("price", out var priceEl) &&
                    priceEl.TryGetProperty("value", out var valueEl))
                {
                    price = decimal.Parse(valueEl.GetString()!);
                }
                else if (item.TryGetProperty("currentBidPrice", out var bidEl) &&
                         bidEl.TryGetProperty("value", out var bidValueEl))
                {
                    price = decimal.Parse(bidValueEl.GetString()!);
                }

                var itemUrl = item.TryGetProperty("itemWebUrl", out var urlEl) ? urlEl.GetString()! : "";
                var condition = item.TryGetProperty("condition", out var condEl) ? condEl.GetString() : null;

                var listingType = "FIXED_PRICE";
                if (item.TryGetProperty("buyingOptions", out var boArr))
                {
                    foreach (var bo in boArr.EnumerateArray())
                    {
                        if (bo.GetString() == "AUCTION") { listingType = "AUCTION"; break; }
                    }
                }

                decimal? shippingCost = null;
                if (item.TryGetProperty("shippingOptions", out var shippingArr) && shippingArr.GetArrayLength() > 0)
                {
                    var firstOption = shippingArr[0];
                    if (firstOption.TryGetProperty("shippingCost", out var scEl) &&
                        scEl.TryGetProperty("value", out var scValEl))
                    {
                        shippingCost = decimal.Parse(scValEl.GetString()!);
                    }
                }

                results.Add(new EbayListing(itemId, title, price, null, itemUrl, condition, listingType, shippingCost));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Skipped one item due to parse error: {ex.Message}");
            }
        }

        return results;
    }
    public async Task<bool> IsListingStillActiveAsync(string itemId)
    {
        var token = await _auth.GetTokenAsync();
        var url = $"https://api.ebay.com/buy/browse/v1/item/{Uri.EscapeDataString(itemId)}?fieldgroups=COMPACT";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-EBAY-C-ENDUSERCTX", "contextualLocation=country=US,zip=77002");

        var response = await _http.SendAsync(request);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return false;

        if (!response.IsSuccessStatusCode)
            return true; // unknown/transient error — don't falsely mark sold

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        // Check estimatedAvailabilities array for OUT_OF_STOCK / availability count
        if (doc.RootElement.TryGetProperty("estimatedAvailabilities", out var availArr) &&
            availArr.GetArrayLength() > 0)
        {
            var status = availArr[0].TryGetProperty("estimatedAvailabilityStatus", out var statusEl)
                ? statusEl.GetString() : null;

            if (status == "OUT_OF_STOCK")
                return false;
        }

        return true;
    }
}