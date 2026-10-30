using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;
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

public record CardIdentifyResult(CardIdentification Card, string RawJson);

public class CardIdentifier
{
    private readonly AnthropicClient _client;
    private readonly string _model;
    private readonly Effort _effort;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string Prompt = """
        You are identifying a sports trading card from photos so it can be listed for sale on eBay.
        Image 1 is the FRONT of the card, image 2 is the BACK.

        Read the card itself (and the grading slab label, if the card is slabbed) and fill in every field.
        - year: the card's season/copyright year as printed (e.g. 2023). The back usually has it.
        - manufacturer: e.g. Topps, Panini, Upper Deck, Bowman, Leaf.
        - set: the product/set name, e.g. "Chrome", "Prizm", "Series 1", "Heritage". Don't repeat the manufacturer.
        - cardNumber: as printed on the back, without '#'. Include insert prefixes like "BCP-12".
        - parallel: the parallel/variant name if it's not the base card (e.g. "Gold Refractor", "Silver Prizm"), otherwise null.
        - serialNumber: the stamped serial numbering like "23/99" only if the card is numbered, otherwise null.
        - isRookie: true if the card has an RC logo or is otherwise clearly a rookie card.
        - gradingCompany / grade: only if the card is in a grading slab (PSA, BGS, SGC, CGC, TAG), otherwise null.
        - conditionNotes: visible condition issues for a raw card (corners, edges, centering, surface), or a short note for a slab.
        - confidence: 0 to 1, how sure you are of the identification overall. Lower it when text is unreadable or you're guessing.
        - suggestedEbayTitle: an eBay title, 80 characters max, keyword style, in this order where available:
          year, manufacturer, set, player, parallel, #cardNumber, serial, RC, Auto/Patch, grading company + grade.
          No filler words like "Wow" or "L@@K".

        Use null for anything you can't determine rather than guessing.
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

    public async Task<CardIdentifyResult> IdentifyAsync(CardImage front, CardImage back, CancellationToken ct = default)
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
                Format = new BetaJsonOutputFormat { Schema = BuildSchema() }
            },
            Messages =
            [
                new BetaUserMessageParam
                {
                    Content = new List<BetaContentBlockParam>
                    {
                        ImageBlock(front),
                        ImageBlock(back),
                        new BetaTextBlockParam { Text = Prompt }
                    }
                }
            ]
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

        return new CardIdentifyResult(ParseAndValidate(json), json);
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

        return card with
        {
            SuggestedEbayTitle = title,
            Confidence = Math.Clamp(card.Confidence, 0m, 1m),
            CardNumber = card.CardNumber?.TrimStart('#').Trim()
        };
    }

    private static Dictionary<string, JsonElement> BuildSchema()
    {
        object NullableString() => new { type = new[] { "string", "null" } };

        var properties = new Dictionary<string, object>
        {
            ["player"] = NullableString(),
            ["year"] = new { type = new[] { "integer", "null" } },
            ["manufacturer"] = NullableString(),
            ["set"] = NullableString(),
            ["cardNumber"] = NullableString(),
            ["parallel"] = NullableString(),
            ["serialNumber"] = NullableString(),
            ["isRookie"] = new { type = "boolean" },
            ["isAutograph"] = new { type = "boolean" },
            ["isRelic"] = new { type = "boolean" },
            ["gradingCompany"] = NullableString(),
            ["grade"] = new { type = new[] { "number", "null" } },
            ["conditionNotes"] = NullableString(),
            ["confidence"] = new { type = "number" },
            ["suggestedEbayTitle"] = new { type = "string" }
        };

        return new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(properties),
            ["required"] = JsonSerializer.SerializeToElement(properties.Keys.ToArray()),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false)
        };
    }
}
