using System.Net.Http.Headers;
using System.Text.Json;

namespace CardScout.Core.Ebay;

public record EbayListing(
    string ItemId, string Title, decimal CurrentPrice,
    DateTime? EndTime, string ItemUrl, string? Condition, string ListingType,
    decimal? ShippingCost);

public class EbaySearchClient
{
    private readonly HttpClient _http;
    private readonly EbayAuthClient _auth;
    private const string BaseballCardsCategoryId = "212";

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
            var endBefore = DateTime.UtcNow.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
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
}