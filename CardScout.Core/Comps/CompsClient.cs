using System.Text.Json;

namespace CardScout.Core.Comps;

public class CompsClient
{
    private readonly HttpClient _http;
    private readonly string _token;
    private DateTime _lastCallTime = DateTime.MinValue;

    public CompsClient(HttpClient http, string token)
    {
        _http = http;
        _token = token;
    }
    public static string BuildSearchUrl(string productName, string consoleName)
    {
        var query = Uri.EscapeDataString($"{productName}");
        return $"https://www.sportscardspro.com/search-products?q={query}&type=prices";
    }
    private async Task ThrottleAsync()
    {
        var elapsed = DateTime.UtcNow - _lastCallTime;
        if (elapsed.TotalMilliseconds < 1100)
            await Task.Delay(1100 - (int)elapsed.TotalMilliseconds);
        _lastCallTime = DateTime.UtcNow;
    }

    public async Task<List<CompCandidate>> SearchAsync(string query)
    {
        await ThrottleAsync();

        var url = $"https://www.sportscardspro.com/api/products?t={_token}&q={Uri.EscapeDataString(query)}";
        var response = await _http.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        var doc = JsonDocument.Parse(body);
        var status = doc.RootElement.GetProperty("status").GetString();

        var results = new List<CompCandidate>();
        if (status != "success" || !doc.RootElement.TryGetProperty("products", out var products))
            return results;

        foreach (var p in products.EnumerateArray())
        {
            results.Add(new CompCandidate
            {
                Id = p.GetProperty("id").GetString() ?? "",
                ProductName = p.TryGetProperty("product-name", out var pn) ? pn.GetString() ?? "" : "",
                ConsoleName = p.TryGetProperty("console-name", out var cn) ? cn.GetString() ?? "" : ""
            });
        }
        return results;
    }

    public async Task<CompDetail?> GetProductAsync(string productId)
    {
        await ThrottleAsync();

        var url = $"https://www.sportscardspro.com/api/product?t={_token}&id={Uri.EscapeDataString(productId)}";
        var response = await _http.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        var doc = JsonDocument.Parse(body);
        if (doc.RootElement.GetProperty("status").GetString() != "success") return null;

        // prices come back as pennies (int) per sportscardspro docs — divide by 100
        decimal? ReadPrice(string key) =>
            doc.RootElement.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.Number
                ? el.GetInt64() / 100m
                : null;

        return new CompDetail
        {
            Id = productId,
            ProductName = doc.RootElement.TryGetProperty("product-name", out var pn) ? pn.GetString() ?? "" : "",
            ConsoleName = doc.RootElement.TryGetProperty("console-name", out var cn) ? cn.GetString() ?? "" : "",
            LoosePrice = ReadPrice("loose-price"),
            GradedPrice = ReadPrice("graded-price"),
            NewPrice = ReadPrice("new-price"),
            CibPrice = ReadPrice("cib-price"),
            ManualOnlyPrice = ReadPrice("manual-only-price"),
            BoxOnlyPrice = ReadPrice("box-only-price"),
            Bgs10Price = ReadPrice("bgs-10-price")
        };
    }
}