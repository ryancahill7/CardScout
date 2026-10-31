using CardScout.Core.Pricing;

namespace CardScout.Core.Flip;

// The user's bar for a flip: at least MinProfit dollars AND MinProfitPct return on cost
public record FlipTargets(decimal MinProfit, decimal MinProfitPct)
{
    public static readonly FlipTargets Default = new(3m, 20m);
}

public static class FlipVerdict
{
    public const string Steal = "STEAL";          // clears the targets with a big margin
    public const string Buy = "BUY";              // clears the targets
    public const string Fair = "FAIR";            // around market: fine to own, not a flip
    public const string Overpriced = "OVERPRICED";
    public const string Unknown = "UNKNOWN";      // no market price to judge by
}

public record FlipAnalysis(
    decimal? MarketPrice,
    decimal? NetProceeds,
    decimal AskingPrice,
    decimal BuyShipping,
    decimal TotalCost,
    decimal? NetProfit,
    decimal? NetProfitPct,
    decimal? PctOfMarket,
    // Most you can pay (before shipping) and still hit the targets; for auctions, the max bid
    decimal? MaxBuyPrice,
    // A rounded opening offer below MaxBuyPrice, for haggling at a show
    decimal? OpeningOffer,
    bool MeetsTargets,
    string Verdict,
    IReadOnlyList<string> Reasons);

// Buy-or-pass math shared by the watchlist worker, the lot analyzer and (mirrored in
// mobile/src/lib/flip.ts) the show-mode screen, so they all agree on the verdict.
public static class FlipAnalyzer
{
    private const decimal StealRoiPct = 50m;
    private const decimal FairMaxPctOfMarket = 105m;
    private const double LowConfidence = 0.7;

    public static FlipAnalysis Analyze(
        decimal? marketPrice, decimal askingPrice, decimal buyShipping, FlipTargets targets, double? matchConfidence = null)
    {
        var totalCost = askingPrice + buyShipping;
        var reasons = new List<string>();

        if (marketPrice is null or <= 0)
        {
            reasons.Add("No market price for this card, so there's nothing to compare against.");
            return new FlipAnalysis(null, null, askingPrice, buyShipping, totalCost, null, null, null, null, null,
                false, FlipVerdict.Unknown, reasons);
        }

        var market = marketPrice.Value;
        var net = Math.Round(SellerFees.NetProceeds(market), 2);
        var profit = Math.Round(net - totalCost, 2);
        var roi = totalCost == 0 ? 0 : Math.Round(profit / totalCost * 100m, 1);
        var pctOfMarket = Math.Round(askingPrice / market * 100m, 1);
        var meets = profit >= targets.MinProfit && roi >= targets.MinProfitPct;

        var maxTotal = Math.Min(net - targets.MinProfit, net / (1 + targets.MinProfitPct / 100m));
        decimal? maxBuy = maxTotal - buyShipping > 0 ? Math.Floor((maxTotal - buyShipping) * 100) / 100 : null;
        decimal? opening = maxBuy is > 0 ? RoundDownNice(maxBuy.Value * 0.85m) : null;

        string verdict;
        if (meets && roi >= StealRoiPct) verdict = FlipVerdict.Steal;
        else if (meets) verdict = FlipVerdict.Buy;
        else if (pctOfMarket <= FairMaxPctOfMarket) verdict = FlipVerdict.Fair;
        else verdict = FlipVerdict.Overpriced;

        reasons.Add($"Sells for about ${market:0.00}, which nets ${net:0.00} after eBay fees.");
        reasons.Add(profit >= 0
            ? $"Flipping it makes ${profit:0.00} ({roi:0.#}% on ${totalCost:0.00})."
            : $"Flipping it loses ${-profit:0.00}.");
        if (!meets && maxBuy != null)
            reasons.Add($"To hit your ${targets.MinProfit:0} / {targets.MinProfitPct:0}% target, pay no more than ${maxBuy:0.00}.");
        if (maxBuy == null)
            reasons.Add("The market price is too low to flip at any price after fees.");
        if (matchConfidence is < LowConfidence)
            reasons.Add($"Comp match is only {matchConfidence * 100:0}% sure, so check it's the same card and parallel.");

        return new FlipAnalysis(market, net, askingPrice, buyShipping, totalCost, profit, roi, pctOfMarket,
            maxBuy, opening, meets, verdict, reasons);
    }

    // A lot is judged as if each card is sold on its own: fees apply per card, and cards
    // that would net nothing (commons under ~$1.50) count as zero rather than negative.
    public static FlipAnalysis AnalyzeLot(
        IReadOnlyCollection<decimal> cardPrices, decimal askingPrice, decimal buyShipping, FlipTargets targets)
    {
        var market = cardPrices.Sum();
        if (market <= 0) return Analyze(null, askingPrice, buyShipping, targets);

        var net = Math.Round(cardPrices.Sum(p => Math.Max(0, SellerFees.NetProceeds(p))), 2);
        var single = Analyze(market, askingPrice, buyShipping, targets);

        // Re-run the money math against the per-card net instead of one big sale
        var totalCost = askingPrice + buyShipping;
        var profit = Math.Round(net - totalCost, 2);
        var roi = totalCost == 0 ? 0 : Math.Round(profit / totalCost * 100m, 1);
        var meets = profit >= targets.MinProfit && roi >= targets.MinProfitPct;
        var maxTotal = Math.Min(net - targets.MinProfit, net / (1 + targets.MinProfitPct / 100m));
        decimal? maxBuy = maxTotal - buyShipping > 0 ? Math.Floor((maxTotal - buyShipping) * 100) / 100 : null;

        var verdict = meets && roi >= StealRoiPct ? FlipVerdict.Steal
            : meets ? FlipVerdict.Buy
            : single.PctOfMarket <= FairMaxPctOfMarket ? FlipVerdict.Fair
            : FlipVerdict.Overpriced;

        var reasons = new List<string>
        {
            cardPrices.Count == 1
                ? $"The one priced card is worth about ${market:0.00} and nets ${net:0.00} after fees."
                : $"The {cardPrices.Count} priced cards are worth about ${market:0.00} together and net ${net:0.00} sold one by one.",
            profit >= 0 ? $"Buying the lot makes ${profit:0.00} ({roi:0.#}%)." : $"Buying the lot loses ${-profit:0.00}."
        };
        if (!meets && maxBuy != null)
            reasons.Add($"To hit your target, pay no more than ${maxBuy:0.00} for the lot.");
        if (maxBuy == null)
            reasons.Add("After fees there isn't enough value here to flip at any price.");

        return single with
        {
            NetProceeds = net,
            NetProfit = profit,
            NetProfitPct = roi,
            MaxBuyPrice = maxBuy,
            OpeningOffer = maxBuy is > 0 ? RoundDownNice(maxBuy.Value * 0.85m) : null,
            MeetsTargets = meets,
            Verdict = verdict,
            Reasons = reasons
        };
    }

    // Offers people actually say out loud: whole dollars under $20, then $5 and $10 steps
    public static decimal RoundDownNice(decimal value) => value switch
    {
        < 1m => Math.Round(value, 2),
        < 20m => Math.Floor(value),
        < 100m => Math.Floor(value / 5m) * 5m,
        _ => Math.Floor(value / 10m) * 10m
    };
}
