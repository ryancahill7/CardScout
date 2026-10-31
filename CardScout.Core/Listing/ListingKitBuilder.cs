using System.Text;
using CardScout.Core.Matching;
using CardScout.Core.Models;
using CardScout.Core.Pricing;

namespace CardScout.Core.Listing;

public record ItemSpecific(string Name, string Value);

public record ListingPricing(
    // The comp the strategy is built on (market value for the card's grade)
    decimal? MarketPrice,
    decimal? BuyItNowPrice,
    decimal? AutoAcceptOffer,
    decimal? AutoDeclineOffer,
    // Lowest sale price that still covers cost + fees
    decimal? BreakEvenPrice,
    decimal? NetAtBuyItNow,
    decimal? ProfitAtBuyItNow,
    string Format,
    string Strategy);

// Everything needed to list a card on eBay by hand: title, item specifics, description
// and a price strategy. Posting through the eBay Sell API needs a per-user OAuth consent
// flow, so for now the app copies these fields and opens eBay's listing page.
public record ListingKit(
    string Title,
    string Category,
    string Condition,
    IReadOnlyList<ItemSpecific> ItemSpecifics,
    string Description,
    ListingPricing Pricing,
    string SellUrl);

public static class ListingKitBuilder
{
    // Below this, fees and shipping supplies eat most of the sale; better sold in a lot
    private const decimal LotThreshold = 5m;

    public static ListingKit Build(CardInventoryItem card, decimal? marketPrice)
    {
        if (TcgMatcher.IsTcg(card.Category)) return BuildTcg(card, marketPrice);

        var graded = card.GradingCompany != null && card.Grade != null;
        var specifics = new List<ItemSpecific>();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) specifics.Add(new ItemSpecific(name, value.Trim()));
        }

        // Sport isn't part of the identification, so the seller picks it on eBay
        Add("Player/Athlete", card.Player);
        Add("Manufacturer", card.Manufacturer);
        Add("Set", string.Join(" ", new[] { card.CardYear?.ToString(), card.Manufacturer, card.SetName }.Where(s => !string.IsNullOrWhiteSpace(s))));
        Add("Season", card.CardYear?.ToString());
        Add("Year Manufactured", card.CardYear?.ToString());
        Add("Card Number", card.CardNumber);
        Add("Parallel/Variety", card.Parallel);
        Add("Print Run", card.SerialNumber?.Contains('/') == true ? card.SerialNumber.Split('/')[1] : null);
        var features = new List<string>();
        if (card.IsRookie == "Y") features.Add("Rookie");
        if (card.SerialNumber != null) features.Add("Serial Numbered");
        if (card.Parallel != null) features.Add("Parallel/Variety");
        Add("Features", features.Count > 0 ? string.Join(", ", features) : null);
        Add("Autographed", card.IsAutograph == "Y" ? "Yes" : "No");
        Add("Memorabilia", card.IsRelic == "Y" ? "Jersey/Patch" : null);
        Add("Graded", graded ? "Yes" : "No");
        if (graded)
        {
            Add("Professional Grader", GraderName(card.GradingCompany!));
            Add("Grade", card.Grade!.Value.ToString("0.#"));
        }
        else
        {
            Add("Card Condition", "Near Mint or Better");
        }
        Add("Type", "Sports Trading Card");
        Add("Original/Licensed Reprint", "Original");

        return new ListingKit(
            card.EbayTitle,
            "Sports Mem, Cards & Fan Shop > Sports Trading Cards > Trading Card Singles",
            graded ? "Graded" : "Ungraded",
            specifics,
            BuildDescription(card, graded),
            BuildPricing(card, marketPrice),
            "https://www.ebay.com/sl/sell");
    }

    // eBay's CCG item specifics: Game, Card Name, Set, Card Number, Rarity, Finish, Language...
    private static ListingKit BuildTcg(CardInventoryItem card, decimal? marketPrice)
    {
        var graded = card.GradingCompany != null && card.Grade != null;
        var specifics = new List<ItemSpecific>();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) specifics.Add(new ItemSpecific(name, value.Trim()));
        }

        Add("Game", TcgMatcher.GameDisplayName(card.Game));
        Add("Card Name", card.Player);
        Add("Set", card.SetName);
        Add("Card Number", card.CardNumber);
        Add("Rarity", card.Rarity);
        Add("Finish", card.Parallel);
        Add("Language", card.CardLanguage ?? "English");
        Add("Manufacturer", card.Manufacturer);
        Add("Year Manufactured", card.CardYear?.ToString());
        Add("Autographed", card.IsAutograph == "Y" ? "Yes" : "No");
        Add("Graded", graded ? "Yes" : "No");
        if (graded)
        {
            Add("Professional Grader", GraderName(card.GradingCompany!));
            Add("Grade", card.Grade!.Value.ToString("0.#"));
            Add("Certification Number", card.CertNumber);
        }
        else
        {
            Add("Card Condition", "Near Mint or Better");
        }

        var category = TcgMatcher.NormalizeGame(card.Game) switch
        {
            "pokemon" => "Toys & Hobbies > Collectible Card Games > CCG Individual Cards (Game: Pokémon TCG)",
            "mtg" => "Toys & Hobbies > Collectible Card Games > CCG Individual Cards (Game: Magic: The Gathering)",
            "yugioh" => "Toys & Hobbies > Collectible Card Games > CCG Individual Cards (Game: Yu-Gi-Oh! TCG)",
            _ => "Toys & Hobbies > Collectible Card Games > CCG Individual Cards"
        };

        var sb = new StringBuilder();
        sb.AppendLine(card.EbayTitle);
        sb.AppendLine();
        var line = string.Join(" ", new[] { TcgMatcher.GameDisplayName(card.Game), card.Player }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (line.Length > 0) sb.AppendLine(line);
        var setLine = string.Join(" · ", new[] { card.SetName, card.CardNumber == null ? null : $"#{card.CardNumber}", card.Rarity }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (setLine.Length > 0) sb.AppendLine(setLine);
        if (card.Parallel != null) sb.AppendLine($"Finish: {card.Parallel}");
        if (card.CardLanguage != null && !card.CardLanguage.Equals("English", StringComparison.OrdinalIgnoreCase))
            sb.AppendLine($"Language: {card.CardLanguage}");
        if (card.IsAutograph == "Y") sb.AppendLine("Autographed");
        if (graded) sb.AppendLine($"Graded {GraderName(card.GradingCompany!)} {card.Grade:0.#}");
        sb.AppendLine();
        if (!graded && card.ConditionNotes != null)
        {
            sb.AppendLine($"Condition: {card.ConditionNotes}");
            sb.AppendLine();
        }
        sb.AppendLine("You'll receive the exact card pictured. Please review the photos, as they're part of the description.");
        sb.AppendLine(graded
            ? "Ships in a padded bubble mailer with the slab protected by bubble wrap."
            : "Ships in a penny sleeve and top loader inside a bubble mailer, with tracking.");
        sb.AppendLine("Ships within 1 business day of payment.");

        return new ListingKit(
            card.EbayTitle,
            category,
            graded ? "Graded" : "Ungraded",
            specifics,
            sb.ToString().Trim(),
            BuildPricing(card, marketPrice),
            "https://www.ebay.com/sl/sell");
    }

    public static ListingPricing BuildPricing(CardInventoryItem card, decimal? marketPrice)
    {
        var cost = (card.CostBasis ?? 0) + (card.GradingCost ?? 0);
        decimal? breakEven = cost > 0 ? Math.Ceiling(SellerFees.SalePriceForNet(cost) * 100) / 100 : null;

        if (marketPrice is null or <= 0)
        {
            return new ListingPricing(null, null, null, null, breakEven, null, null, "Buy It Now",
                "There's no market comp for this card. Look up recent sold listings on eBay and price just under the lowest sale.");
        }

        var market = marketPrice.Value;
        if (market < LotThreshold)
        {
            return new ListingPricing(market, Charm(market), null, null, breakEven, Round(SellerFees.NetProceeds(Charm(market))),
                Round(SellerFees.NetProceeds(Charm(market)) - cost), "Lot",
                $"At about ${market:0.00} this card nets very little after eBay's fees and the $0.40 order fee. " +
                (TcgMatcher.IsTcg(card.Category)
                    ? "Bundle it with similar cards (same set, type or character) into a lot, or sell it in person."
                    : "Bundle it with similar cards (same player, team or set) into a lot, or sell it in person."));
        }

        // List a bit above market so there's room to accept offers that land at market
        var bin = Charm(market * 1.08m);
        var autoAccept = Math.Floor(market * 0.97m);
        var autoDecline = Math.Floor(Math.Max(market * 0.82m, breakEven ?? 0));
        if (autoDecline >= autoAccept) autoDecline = Math.Max(0, autoAccept - 1);

        var net = Round(SellerFees.NetProceeds(bin));
        var strategy = new StringBuilder();
        strategy.Append($"Buy It Now at ${bin:0.00} with Best Offer on. Auto-accept offers of ${autoAccept:0} or more and auto-decline anything under ${autoDecline:0}. ");
        if (breakEven != null && breakEven > market)
            strategy.Append($"Market (${market:0.00}) is below your break-even of ${breakEven:0.00}, so selling now locks in a loss. ");
        if (market >= 100)
            strategy.Append("For a card this valuable, a 7-day auction ending Sunday evening can beat a fixed price if there's demand. ");
        strategy.Append("If it hasn't sold in 30 days, drop the price about 5%.");

        return new ListingPricing(market, bin, autoAccept, autoDecline, breakEven, net, Round(net - cost),
            "Buy It Now + Best Offer", strategy.ToString().Trim());
    }

    private static string BuildDescription(CardInventoryItem card, bool graded)
    {
        var sb = new StringBuilder();
        sb.AppendLine(card.EbayTitle);
        sb.AppendLine();
        var line = string.Join(" ", new[] { card.CardYear?.ToString(), card.Manufacturer, card.SetName, card.Player }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (line.Length > 0) sb.AppendLine(line);
        if (card.CardNumber != null) sb.AppendLine($"Card #{card.CardNumber}");
        if (card.Parallel != null) sb.AppendLine($"Parallel: {card.Parallel}");
        if (card.SerialNumber != null) sb.AppendLine($"Serial numbered {card.SerialNumber}");
        if (card.IsRookie == "Y") sb.AppendLine("Rookie card (RC)");
        if (card.IsAutograph == "Y") sb.AppendLine("Autographed");
        if (graded) sb.AppendLine($"Graded {GraderName(card.GradingCompany!)} {card.Grade:0.#}");
        sb.AppendLine();
        if (!graded && card.ConditionNotes != null)
        {
            sb.AppendLine($"Condition: {card.ConditionNotes}");
            sb.AppendLine();
        }
        sb.AppendLine("You'll receive the exact card pictured. Please review the photos, as they're part of the description.");
        sb.AppendLine(graded
            ? "Ships in a padded bubble mailer with the slab protected by bubble wrap."
            : "Ships in a penny sleeve and top loader inside a bubble mailer, with tracking.");
        sb.AppendLine("Ships within 1 business day of payment.");
        return sb.ToString().Trim();
    }

    private static string GraderName(string code) => code.ToUpperInvariant() switch
    {
        "PSA" => "Professional Sports Authenticator (PSA)",
        "BGS" => "Beckett Grading Services (BGS)",
        "SGC" => "Sportscard Guaranty Corporation (SGC)",
        "CGC" => "Certified Guaranty Company (CGC)",
        "TAG" => "Technical Authentication & Grading (TAG)",
        _ => code
    };

    // $12.40 -> $12.99, $47 -> $46.99: prices end in .99 like most eBay listings
    private static decimal Charm(decimal price) => price < 1 ? 0.99m : Math.Floor(price) + 0.99m - (price % 1 == 0 ? 1 : 0);

    private static decimal Round(decimal value) => Math.Round(value, 2);
}
