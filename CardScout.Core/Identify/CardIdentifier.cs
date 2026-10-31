using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using CardScout.Core.Matching;
using CardScout.Core.Models;

namespace CardScout.Core.Identify;

public class CardIdentificationException : Exception
{
    public string? RawResponse { get; }

    public CardIdentificationException(string message, string? rawResponse = null, Exception? inner = null)
        : base(message, inner)
    {
        RawResponse = rawResponse;
    }
}

public record CardImage(byte[] Data, string MediaType);

// Tokens billed for one identification call, for cost tracking
public record ScanUsage(string Model, long InputTokens, long OutputTokens);

public record CardIdentifyResult(CardIdentification Card, string RawJson, ScanUsage? Usage = null);

public record LotIdentifyResult(IReadOnlyList<LotCardIdentification> Cards, string? Notes, string RawJson, ScanUsage? Usage = null);

public class CardIdentifier
{
    private readonly AnthropicClient _client;
    private readonly string _model;
    private readonly Effort _effort;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string FrontAndBack = "Image 1 is the FRONT of the card, image 2 is the BACK.";
    private const string FrontOnly =
        "The image is the FRONT of the card only. The back wasn't photographed, so infer the year and card number from the front where you can.";

    private const string Prompt = """
        You are identifying a trading card from photos so it can be priced and listed for sale on eBay.
        It is either a SPORTS card (baseball, basketball, football, hockey, soccer...) or a trading card game (TCG) card
        (Pokemon, Magic: The Gathering, Yu-Gi-Oh!, One Piece Card Game, Disney Lorcana, or another TCG).
        {{IMAGES}}

        Read the card itself (and the grading slab label, if the card is slabbed) and fill in every field.
        - category: "sports" or "tcg". Decide from the front: TCG cards have game text, HP/attacks, mana costs, ATK/DEF,
          ink costs or a game logo; sports cards show an athlete.
        - game: for TCG cards one of "pokemon", "mtg", "yugioh", "one-piece", "lorcana", or "other". Null for sports cards.

        For SPORTS cards:
        - player: the athlete's name.
        - year: the card's season/copyright year as printed (e.g. 2023). The back usually has it.
        - manufacturer: e.g. Topps, Panini, Upper Deck, Bowman, Leaf.
        - set: the product/set name, e.g. "Chrome", "Prizm", "Series 1", "Heritage". Don't repeat the manufacturer.
        - cardNumber: as printed on the back, without '#'. Include insert prefixes like "BCP-12".
        - parallel: the parallel/variant name if it's not the base card (e.g. "Gold Refractor", "Silver Prizm"), otherwise null.
        - isRookie: true if the card has an RC logo or is otherwise clearly a rookie card.
        - setCode, rarity: null. language: null unless the card isn't in English.

        For TCG cards (the back is the same for every card in the game, so everything comes from the front):
        - player: the card's name as printed, with its suffix (e.g. "Charizard", "Pikachu ex", "Umbreon VMAX",
          "Black Lotus", "Blue-Eyes White Dragon", "Monkey.D.Luffy", "Elsa - Spirit of Winter"). Use the English name
          for a non-English card when you know it.
        - year: the copyright year printed on the card, if readable.
        - manufacturer: the publisher, e.g. "The Pokemon Company", "Wizards of the Coast", "Konami", "Bandai", "Ravensburger".
        - set: the English set/expansion name, e.g. "Base Set", "Evolving Skies", "Scarlet & Violet 151", "Legend of Blue
          Eyes White Dragon", "Romance Dawn", "The First Chapter". Work it out from the set symbol, set code, card number
          total and card design. Don't repeat the game name.
        - setCode: the printed set code if there is one (e.g. "LOB", "OP01", "MH3", "SV3PT5", "PAF"), otherwise null.
        - cardNumber: the collector number exactly as printed, including the set total or code (e.g. "4/102", "215/203",
          "TG05/TG30", "LOB-EN001", "OP01-024", "123/204"). No '#'.
        - parallel: the printing variant or finish ONLY when it differs from the standard printing of that card number:
          "1st Edition", "Shadowless", "Reverse Holo", "Foil" (Magic/Lorcana: a foil of a non-foil card), "Etched Foil",
          "Borderless", "Extended Art", "Showcase", "Alternate Art" or "Parallel" (One Piece), "Poke Ball", "Master Ball",
          "Cosmos Holo", "Staff", "Prerelease", "Promo stamp". A holo rare that only exists as a holo is the standard
          printing, so leave parallel null. Unlimited printings of WOTC Pokemon (no 1st Edition stamp) are null.
        - rarity: as printed or shown by the rarity symbol, e.g. "Common", "Rare Holo", "Illustration Rare",
          "Special Illustration Rare", "Hyper Rare", "Mythic Rare", "Secret Rare", "Ultra Rare", "Enchanted", "Super Rare".
        - language: the card's language, e.g. "English", "Japanese", "Korean", "Chinese", "German".
        - isRookie and isRelic: false. isAutograph: true only for a signed card.

        For ALL cards:
        - serialNumber: the stamped serial numbering like "23/99" only if the card is numbered, otherwise null.
          (A TCG collector number like 4/102 is cardNumber, not serialNumber.)
        - gradingCompany / grade: only if the card is in a grading slab (PSA, BGS, SGC, CGC, TAG, ACE), otherwise null.
        - certNumber: for a slabbed card, the certification number printed on the slab label (PSA calls it "Cert #",
          usually 8-9 digits; BGS/SGC/CGC/TAG print a similar serial or barcode number). Digits only, no spaces.
          This is different from serialNumber (the card's own "/99" numbering). Null for raw cards or if unreadable.
        - conditionNotes: visible condition issues for a raw card (corners, edges, centering, surface, edge whitening),
          or a short note for a slab.
        - gradeOutlook: for a RAW card, your estimate of the chance it would grade PSA 10, PSA 9, PSA 8, and 7 or lower,
          judged from centering (left/right and top/bottom), corners, edges and surface. The four numbers must add up to 1.
          Be conservative: most pack-fresh modern cards are not 10s, and photos hide surface flaws (holo scratches too).
          summary: one short sentence on what drives the estimate (e.g. "Centering about 60/40 left-right, sharp corners").
          For a slabbed card, set all four numbers to 0 and summary to null.
        - confidence: 0 to 1, how sure you are of the identification overall. Lower it when text is unreadable or you're guessing.
        - suggestedEbayTitle: an eBay title, 80 characters max, keyword style, no filler words like "Wow" or "L@@K".
          Sports order: year, manufacturer, set, player, parallel, #cardNumber, serial, RC, Auto/Patch, grading company + grade.
          TCG order: game (Pokemon / MTG / Yu-Gi-Oh! / One Piece / Lorcana), card name, cardNumber, set, variant,
          rarity, language if not English, grading company + grade. E.g. "Pokemon Charizard 4/102 Base Set Holo Rare PSA 8".

        Use null for anything you can't determine rather than guessing.
        """;

    private const string LotPrompt = """
        This photo shows several trading cards (a lot, a binder page, or a dealer's display case). They can be sports
        cards or trading card game (TCG) cards such as Pokemon, Magic: The Gathering, Yu-Gi-Oh!, One Piece or Lorcana.
        List every card whose front is visible, one entry per card, in reading order (left to right, top to bottom).

        For each card:
        - category: "sports" or "tcg". game: for TCG one of "pokemon", "mtg", "yugioh", "one-piece", "lorcana", "other"; null for sports.
        - player: the athlete for sports cards; the card name as printed for TCG cards (e.g. "Pikachu ex", "Black Lotus").
        - year, manufacturer, set, cardNumber, parallel: read them from the card where you can. Recognizable
          design cues (set design, set symbols, logos, border colors) are fine to use for the year and set. cardNumber
          without '#'; for TCG cards the collector number as printed (e.g. "4/102", "OP01-024"). For TCG cards, parallel
          is the printing variant (1st Edition, Reverse Holo, Foil, Alternate Art) or null for the standard printing.
        - language: for TCG cards the card's language (e.g. "English", "Japanese"); null for sports cards.
        - isRookie / isAutograph: from the RC logo or a visible signature.
        - gradingCompany / grade: only if the card is in a grading slab.
        - confidence: 0 to 1, how sure you are of this card's identity. Lower it for small, blurry or partly hidden cards.
        - position: where it is in the photo, e.g. "top row, 2nd from left".
        notes: anything a buyer should know about the lot as a whole (e.g. "several cards are cut off", "looks like a reprint").

        Use null for anything you can't read rather than guessing. Leave out cards you can't identify at all.
        """;

    public CardIdentifier(string apiKey, string model, string effort)
    {
        _client = new AnthropicClient { ApiKey = apiKey };
        _model = model;
        _effort = effort.ToLowerInvariant() switch
        {
            "low" => Effort.Low,
            "high" => Effort.High,
            "max" => Effort.Max,
            _ => Effort.Medium
        };
    }

    // back is optional: show mode photographs the front only, for speed
    public async Task<CardIdentifyResult> IdentifyAsync(CardImage front, CardImage? back, CancellationToken ct = default)
    {
        var content = new List<BetaContentBlockParam> { ImageBlock(front) };
        if (back != null) content.Add(ImageBlock(back));
        content.Add(new BetaTextBlockParam { Text = Prompt.Replace("{{IMAGES}}", back != null ? FrontAndBack : FrontOnly) });

        var (json, usage) = await SendAsync(content, BuildSchema(), ct);
        return new CardIdentifyResult(ParseAndValidate(json), json, usage);
    }

    public async Task<LotIdentifyResult> IdentifyLotAsync(CardImage photo, CancellationToken ct = default)
    {
        var content = new List<BetaContentBlockParam> { ImageBlock(photo), new BetaTextBlockParam { Text = LotPrompt } };
        var (json, usage) = await SendAsync(content, BuildLotSchema(), ct);

        LotJson? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<LotJson>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new CardIdentificationException("The model's response wasn't valid lot JSON.", json, ex);
        }
        if (parsed == null) throw new CardIdentificationException("The model returned an empty response.", json);

        var cards = parsed.Cards
            .Select(c => c with
            {
                Confidence = Math.Clamp(c.Confidence, 0m, 1m),
                CardNumber = c.CardNumber?.TrimStart('#').Trim(),
                Category = TcgMatcher.NormalizeCategory(c.Category),
                Game = TcgMatcher.IsTcg(c.Category) ? TcgMatcher.NormalizeGame(c.Game) ?? "other" : null
            })
            .ToList();
        return new LotIdentifyResult(cards, parsed.Notes, json, usage);
    }

    private record LotJson(List<LotCardIdentification> Cards, string? Notes);

    private async Task<(string Json, ScanUsage Usage)> SendAsync(
        List<BetaContentBlockParam> content, Dictionary<string, JsonElement> schema, CancellationToken ct)
    {
        var parameters = new MessageCreateParams
        {
            Model = _model,
            MaxTokens = 16000,
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),
            OutputConfig = new BetaOutputConfig
            {
                Effort = _effort,
                Format = new BetaJsonOutputFormat { Schema = schema }
            },
            Messages = [new BetaUserMessageParam { Content = content }]
        };

        var response = await _client.Beta.Messages.Create(parameters, ct);

        if (response.StopReason == BetaStopReason.Refusal)
            throw new CardIdentificationException("The model declined to identify this card.");
        if (response.StopReason == BetaStopReason.MaxTokens)
            throw new CardIdentificationException("The model's response was cut off before it finished.");

        var json = string.Concat(response.Content
            .Select(b => b.Value)
            .OfType<BetaTextBlock>()
            .Select(t => t.Text));
        // Thinking tokens are billed as output, so OutputTokens is the full output bill
        return (json, new ScanUsage(response.Model, response.Usage.InputTokens, response.Usage.OutputTokens));
    }

    private static BetaImageBlockParam ImageBlock(CardImage image) => new()
    {
        Source = new BetaBase64ImageSource
        {
            Data = Convert.ToBase64String(image.Data),
            MediaType = image.MediaType
        }
    };

    private static CardIdentification ParseAndValidate(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new CardIdentificationException("The model returned an empty response.");

        CardIdentification? card;
        try
        {
            card = JsonSerializer.Deserialize<CardIdentification>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new CardIdentificationException("The model's response wasn't valid card JSON.", json, ex);
        }

        if (card == null || string.IsNullOrWhiteSpace(card.SuggestedEbayTitle))
            throw new CardIdentificationException("The model's response was missing a suggested title.", json);

        // Structured output guarantees the shape, not these limits, so enforce them here
        var title = card.SuggestedEbayTitle.Trim();
        if (title.Length > 80) title = title[..80].TrimEnd();

        var isTcg = TcgMatcher.IsTcg(card.Category);
        return card with
        {
            Category = TcgMatcher.NormalizeCategory(card.Category),
            Game = isTcg ? TcgMatcher.NormalizeGame(card.Game) ?? "other" : null,
            SetCode = isTcg ? Blank(card.SetCode)?.ToUpperInvariant() : null,
            Rarity = Blank(card.Rarity),
            Language = Blank(card.Language),
            SuggestedEbayTitle = title,
            Confidence = Math.Clamp(card.Confidence, 0m, 1m),
            CardNumber = card.CardNumber?.TrimStart('#').Trim(),
            CertNumber = card.GradingCompany == null ? null : NormalizeCert(card.CertNumber),
            GradeOutlook = card.GradingCompany != null ? null : Normalize(card.GradeOutlook)
        };
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? NormalizeCert(string? cert)
    {
        if (string.IsNullOrWhiteSpace(cert)) return null;
        var cleaned = new string(cert.Where(char.IsLetterOrDigit).ToArray());
        return cleaned.Length is >= 4 and <= 30 ? cleaned : null;
    }

    // The model is asked for probabilities that sum to 1; make sure they do
    private static GradeOutlook? Normalize(GradeOutlook? outlook)
    {
        if (outlook == null) return null;
        var parts = new[] { outlook.Gem10, outlook.Mint9, outlook.NearMint8, outlook.Lower }.Select(p => Math.Max(0m, p)).ToArray();
        var total = parts.Sum();
        if (total <= 0) return null;
        return outlook with
        {
            Gem10 = Math.Round(parts[0] / total, 3),
            Mint9 = Math.Round(parts[1] / total, 3),
            NearMint8 = Math.Round(parts[2] / total, 3),
            Lower = Math.Round(parts[3] / total, 3)
        };
    }

    private static object NullableString() => new { type = new[] { "string", "null" } };

    private static object CategoryEnum() => new { type = "string", @enum = new[] { "sports", "tcg" } };

    // Null for sports cards. Structured outputs don't accept an enum on a ["string","null"] type, so anyOf.
    private static object GameEnum() => new
    {
        anyOf = new object[]
        {
            new { type = "string", @enum = new[] { "pokemon", "mtg", "yugioh", "one-piece", "lorcana", "other" } },
            new { type = "null" }
        }
    };

    private static Dictionary<string, JsonElement> ObjectSchema(Dictionary<string, object> properties) => new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(properties),
        ["required"] = JsonSerializer.SerializeToElement(properties.Keys.ToArray()),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false)
    };

    private static Dictionary<string, JsonElement> BuildSchema()
    {
        var outlook = new Dictionary<string, object>
        {
            ["gem10"] = new { type = "number" },
            ["mint9"] = new { type = "number" },
            ["nearMint8"] = new { type = "number" },
            ["lower"] = new { type = "number" },
            ["summary"] = NullableString()
        };

        var properties = new Dictionary<string, object>
        {
            ["category"] = CategoryEnum(),
            ["game"] = GameEnum(),
            ["player"] = NullableString(),
            ["year"] = new { type = new[] { "integer", "null" } },
            ["manufacturer"] = NullableString(),
            ["set"] = NullableString(),
            ["setCode"] = NullableString(),
            ["cardNumber"] = NullableString(),
            ["parallel"] = NullableString(),
            ["rarity"] = NullableString(),
            ["language"] = NullableString(),
            ["serialNumber"] = NullableString(),
            ["isRookie"] = new { type = "boolean" },
            ["isAutograph"] = new { type = "boolean" },
            ["isRelic"] = new { type = "boolean" },
            ["gradingCompany"] = NullableString(),
            ["grade"] = new { type = new[] { "number", "null" } },
            ["certNumber"] = NullableString(),
            ["conditionNotes"] = NullableString(),
            ["gradeOutlook"] = ObjectSchema(outlook),
            ["confidence"] = new { type = "number" },
            ["suggestedEbayTitle"] = new { type = "string" }
        };

        return ObjectSchema(properties);
    }

    private static Dictionary<string, JsonElement> BuildLotSchema()
    {
        var card = new Dictionary<string, object>
        {
            ["category"] = CategoryEnum(),
            ["game"] = GameEnum(),
            ["language"] = NullableString(),
            ["player"] = NullableString(),
            ["year"] = new { type = new[] { "integer", "null" } },
            ["manufacturer"] = NullableString(),
            ["set"] = NullableString(),
            ["cardNumber"] = NullableString(),
            ["parallel"] = NullableString(),
            ["isRookie"] = new { type = "boolean" },
            ["isAutograph"] = new { type = "boolean" },
            ["gradingCompany"] = NullableString(),
            ["grade"] = new { type = new[] { "number", "null" } },
            ["confidence"] = new { type = "number" },
            ["position"] = NullableString()
        };

        var properties = new Dictionary<string, object>
        {
            ["cards"] = new { type = "array", items = ObjectSchema(card) },
            ["notes"] = NullableString()
        };

        return ObjectSchema(properties);
    }
}
