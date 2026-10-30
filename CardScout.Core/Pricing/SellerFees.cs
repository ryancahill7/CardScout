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
}
