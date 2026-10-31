namespace CardScout.Core.Pricing;

// What it costs to sell a card on eBay. Shared by MatcherService (deal math) and
// CardPricingService (what a card we own would net us).
public static class SellerFees
{
    public const decimal EbayFeeRate = 0.1325m;
    public const decimal EbayPerOrderFee = 0.40m;
    public const decimal PackagingMaterialsCost = 0.75m;

    public static decimal NetProceeds(decimal salePrice) =>
        salePrice * (1 - EbayFeeRate) - EbayPerOrderFee - PackagingMaterialsCost;

    // The sale price that nets exactly `net` (inverse of NetProceeds); used for break-even floors
    public static decimal SalePriceForNet(decimal net) =>
        (net + EbayPerOrderFee + PackagingMaterialsCost) / (1 - EbayFeeRate);

    // What a seller usually charges to ship a card to us, until eBay tells us the real cost
    public static decimal EstimateBuyShipping(decimal price)
    {
        return price switch
        {
            < 10m => 1.30m,   // plain tracked envelope (USPS Ground Advantage letter-ish rate)
            < 30m => 3.50m,   // small bubble mailer
            < 100m => 5.00m,  // padded mailer, toploader, team bag
            _ => 7.50m         // higher-value item, more careful packaging
        };
    }
}
