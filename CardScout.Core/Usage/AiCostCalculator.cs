using System.Globalization;
using CardScout.Core.Identify;
using Microsoft.Extensions.Configuration;

namespace CardScout.Core.Usage;

// USD per million tokens for one model
public record ModelPrice(decimal InputPerMTok, decimal OutputPerMTok, decimal CacheWritePerMTok, decimal CacheReadPerMTok);

// Turns a call's token counts into dollars. Prices are Anthropic's first-party list prices
// (cache writes are the 5-minute TTL rate, 1.25x input). Override or add models in config:
// AnthropicPricing:{model-id}:{InputPerMTok|OutputPerMTok|CacheWritePerMTok|CacheReadPerMTok}.
public class AiCostCalculator
{
    public static readonly IReadOnlyDictionary<string, ModelPrice> DefaultPrices = new Dictionary<string, ModelPrice>
    {
        ["claude-fable-5-1"] = new(10m, 50m, 12.50m, 0.25m),
        ["claude-fable-5"] = new(10m, 50m, 12.50m, 1.00m),
        ["claude-opus-5-5"] = new(4m, 20m, 5m, 0.20m),
        ["claude-opus-5"] = new(5m, 25m, 6.25m, 0.50m),
        ["claude-opus-4-8"] = new(5m, 25m, 6.25m, 0.50m),
        ["claude-sonnet-5-5"] = new(2m, 10m, 2.50m, 0.20m),
        ["claude-sonnet-5"] = new(2m, 10m, 2.50m, 0.20m),
        ["claude-sonnet-4-6"] = new(3m, 15m, 3.75m, 0.30m),
        ["claude-haiku-5-5"] = new(0.10m, 0.50m, 0.125m, 0.01m),
        ["claude-haiku-4-5"] = new(1m, 5m, 1.25m, 0.10m)
    };

    private readonly Dictionary<string, ModelPrice> _prices;
    private readonly ModelPrice _unknownModelPrice;

    public AiCostCalculator(IReadOnlyDictionary<string, ModelPrice> prices)
    {
        _prices = new Dictionary<string, ModelPrice>(prices, StringComparer.OrdinalIgnoreCase);
        // Charge an unpriced model at the most expensive known rate, so the cap errs on the safe side
        _unknownModelPrice = _prices.Values.MaxBy(p => p.OutputPerMTok) ?? new ModelPrice(10m, 50m, 12.50m, 1m);
    }

    public static AiCostCalculator FromConfiguration(IConfiguration config)
    {
        var prices = new Dictionary<string, ModelPrice>(DefaultPrices, StringComparer.OrdinalIgnoreCase);
        foreach (var model in config.GetSection("AnthropicPricing").GetChildren())
        {
            prices.TryGetValue(model.Key, out var current);
            current ??= new ModelPrice(0, 0, 0, 0);
            prices[model.Key] = new ModelPrice(
                Decimal(model["InputPerMTok"]) ?? current.InputPerMTok,
                Decimal(model["OutputPerMTok"]) ?? current.OutputPerMTok,
                Decimal(model["CacheWritePerMTok"]) ?? current.CacheWritePerMTok,
                Decimal(model["CacheReadPerMTok"]) ?? current.CacheReadPerMTok);
        }
        return new AiCostCalculator(prices);
    }

    public bool IsKnownModel(string model) => Find(model) != null;

    public decimal Cost(ScanUsage usage)
    {
        var price = Find(usage.Model) ?? _unknownModelPrice;
        var cost = usage.InputTokens * price.InputPerMTok
                   + usage.OutputTokens * price.OutputPerMTok
                   + usage.CacheWriteTokens * price.CacheWritePerMTok
                   + usage.CacheReadTokens * price.CacheReadPerMTok;
        return Math.Round(cost / 1_000_000m, 6);
    }

    // Exact id first, then the longest configured id the response's model starts with
    // (covers dated or suffixed ids the API may echo back)
    private ModelPrice? Find(string model)
    {
        if (_prices.TryGetValue(model, out var exact)) return exact;
        return _prices
            .Where(kv => model.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kv => kv.Key.Length)
            .Select(kv => kv.Value)
            .FirstOrDefault();
    }

    private static decimal? Decimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
}
