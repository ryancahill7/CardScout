using System.Text.Json;

namespace CardScout.Core.Comps;

public class CompsClient
{
    private readonly HttpClient _http;
    private readonly string _token;
    private DateTime _lastCallTime = DateTime.MinValue;
    // The API serves concurrent requests, so the ~1 call/sec limit needs a lock
    private readonly SemaphoreSlim _throttleLock = new(1, 1);

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
        await _throttleLock.WaitAsync();
        try
        {
            var elapsed = DateTime.UtcNow - _lastCallTime;
            if (elapsed.TotalMilliseconds < 1100)
                await Task.Delay(1100 - (int)elapsed.TotalMilliseconds);
            _lastCallTime = DateTime.UtcNow;
        }
        finally
        {
            _throttleLock.Release();
        }
    }

    // An error status (expired token, rate limit, outage) used to look like "no match",
    // which silently marked every listing NO_MATCH. Throw instead so callers can tell.
    private static JsonDocument ParseOrThrow(HttpResponseMessage response, string body)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new CompsApiException($"SportsCardsPro returned HTTP {(int)response.StatusCode} with a non-JSON body.");
        }

        var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
        if (status != "success")
        {
            var message = doc.RootElement.TryGetProperty("error-message", out var m) ? m.GetString() : null;
            throw new CompsApiException($"SportsCardsPro error (HTTP {(int)response.StatusCode}): {message ?? status ?? "unknown"}");
        }
        return doc;
    }

    public async Task<List<CompCandidate>> SearchAsync(string query)
    {
        await ThrottleAsync();

        var url = $"https://www.sportscardspro.com/api/products?t={_token}&q={Uri.EscapeDataString(query)}";
        var response = await _http.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        var doc = ParseOrThrow(response, body);

        var results = new List<CompCandidate>();
        if (!doc.RootElement.TryGetProperty("products", out var products))
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

        var doc = ParseOrThrow(response, body);

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

public class CompsApiException(string message) : Exception(message);
