using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CardScout.Core.Models;

namespace CardScout.Core.Matching;

// What we know about a trading card game card (Pokemon, Magic, Yu-Gi-Oh!, One Piece, Lorcana...),
// either from structured identification fields or parsed out of an eBay title.
public class TcgQuery
{
    public string? Game { get; set; }
    public string? Name { get; set; }
    public string? Set { get; set; }
    // Collector number as printed: "4/102", "LOB-EN001", "OP01-024", "215"
    public string? Number { get; set; }
    public string? Variant { get; set; }
    // Some games bracket a rarity rather than a printing ("[Enchanted]" in Lorcana)
    public string? Rarity { get; set; }
    public string? Language { get; set; }
    public (string GradingCompany, decimal Grade)? Grade { get; set; }
    // Free text (an eBay title) when there are no structured fields
    public string? Text { get; set; }
}

// PriceCharting (SportsCardsPro's sister site; SportsCardsPro's own search only has sports
// cards) lists TCG cards with the game, language and set in the console name ("Pokemon Base
// Set", "Pokemon Japanese Expansion Pack", "Magic Dominaria United", "YuGiOh Legend of Blue
// Eyes White Dragon", "One Piece Romance Dawn", "Lorcana First Chapter") and the card name, a
// bracketed variant and the number in the product name ("Charizard [1st Edition] #4",
// "Sheoldred, the Apocalypse [Showcase] #369"; Yu-Gi-Oh! and One Piece codes have no '#':
// "Blue-Eyes White Dragon [1st Edition] LOB-001", "Monkey.D.Luffy [Alternate Art] OP01-024").
// Sports matching leans on year, player and parallel colors; this scores on name, set,
// number, variant and language instead.
public static class TcgMatcher
{
    public const string Sports = "sports";
    public const string Tcg = "tcg";

    public static readonly string[] Games = ["pokemon", "mtg", "yugioh", "one-piece", "lorcana", "other"];

    public static bool IsTcg(string? category) => string.Equals(category?.Trim(), Tcg, StringComparison.OrdinalIgnoreCase);

    public static bool IsTcg(CardIdentification card) => IsTcg(card.Category);

    // "sports" | "tcg"; anything else (including null, older rows) is sports
    public static string NormalizeCategory(string? category) => IsTcg(category) ? Tcg : Sports;

    public static string? NormalizeGame(string? game)
    {
        if (string.IsNullOrWhiteSpace(game)) return null;
        var g = Fold(game).Replace(" ", "").Replace("-", "").Replace("!", "").Replace(":", "");
        return g switch
        {
            "pokemon" or "pokemontcg" => "pokemon",
            "mtg" or "magic" or "magicthegathering" => "mtg",
            "yugioh" or "ygo" => "yugioh",
            "onepiece" or "onepiececardgame" => "one-piece",
            "lorcana" or "disneylorcana" => "lorcana",
            _ => "other"
        };
    }

    // The word SportsCardsPro console names and eBay titles use for the game
    public static string? GameWord(string? game) => NormalizeGame(game) switch
    {
        "pokemon" => "Pokemon",
        "mtg" => "Magic",
        "yugioh" => "YuGiOh",
        "one-piece" => "One Piece",
        "lorcana" => "Lorcana",
        _ => null
    };

    public static string? GameDisplayName(string? game) => NormalizeGame(game) switch
    {
        "pokemon" => "Pokémon TCG",
        "mtg" => "Magic: The Gathering",
        "yugioh" => "Yu-Gi-Oh! TCG",
        "one-piece" => "One Piece Card Game",
        "lorcana" => "Disney Lorcana",
        _ => null
    };

    public static TcgQuery FromCard(CardIdentification card) => new()
    {
        Game = NormalizeGame(card.Game),
        Name = card.Player?.Trim(),
        Set = SetAlias(NormalizeGame(card.Game), card.Set?.Trim(), card.Language),
        Number = card.CardNumber?.TrimStart('#').Trim(),
        Variant = card.Parallel?.Trim(),
        Rarity = card.Rarity?.Trim(),
        Language = card.Language?.Trim(),
        Grade = card.GradingCompany != null && card.Grade != null
            ? (card.GradingCompany.Trim().ToUpperInvariant(), card.Grade.Value)
            : null
    };

    // Where PriceCharting's set name differs from what's printed / commonly used
    private static string? SetAlias(string? game, string? set, string? language)
    {
        if (set == null) return null;
        // The Japanese base set is "Expansion Pack" on PriceCharting
        if (game == "pokemon" && LanguageWord(language) == "Japanese" && Fold(set) is "base set" or "base" or "expansion pack base set")
            return "Expansion Pack";
        return set;
    }

    // ---- eBay titles ----

    private static readonly Regex GradeRegex = new(@"\b(PSA|BGS|SGC|CGC|TAG|ACE|BECKETT)\s*(\d+(\.\d)?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 4/102, 025/198, TG05/TG30, GG44/GG70, SV-P 049 isn't covered (rare)
    private static readonly Regex SlashNumber = new(@"\b([A-Z]{0,3}\d{1,4}[a-z]?)\s*/\s*([A-Z]{0,3}\d{1,4})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // LOB-EN001, OP01-024, ST10-005, SWSH050, MEW-EN, EB01-001
    private static readonly Regex CodeNumber = new(@"\b([A-Z]{2,5}\d{0,2})-([A-Z]{0,2}\d{2,4}[A-Z]?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HashNumber = new(@"#\s*([A-Z]{0,5}\d{0,2}-?[A-Z]{0,2}\d{1,4}[A-Z]?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Variants SportsCardsPro prices separately (as a [bracket] in the product name)
    private static readonly string[] VariantTerms =
    [
        "1st edition", "shadowless", "reverse holo", "reverse foil", "cosmos holo", "poke ball", "master ball",
        "foil", "etched", "borderless", "extended art", "showcase", "alternate art", "alt art", "full art",
        "parallel", "manga", "staff", "prerelease", "stamped", "promo", "error", "unlimited", "galaxy"
    ];

    private static readonly Regex NoiseWords = new(
        @"\b(nm|near mint|mint|lp|mp|hp|dmg|rare|holo rare|tcg|ccg|card|cards|single|english|free shipping|l@@k|wow|hot|invest|pack fresh|psa ready|gem)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static TcgQuery ParseTitle(string title, string? game)
    {
        var q = new TcgQuery { Text = title, Game = NormalizeGame(game) };

        var grade = GradeRegex.Match(title);
        if (grade.Success)
            q.Grade = (grade.Groups[1].Value.ToUpperInvariant() == "BECKETT" ? "BGS" : grade.Groups[1].Value.ToUpperInvariant(),
                decimal.Parse(grade.Groups[2].Value, CultureInfo.InvariantCulture));

        var withoutGrade = GradeRegex.Replace(title, " ");
        q.Number = ExtractNumber(withoutGrade);

        var folded = Fold(title);
        q.Language = Languages.FirstOrDefault(l => Regex.IsMatch(folded, $@"\b{l}\b"));
        if (q.Language == null && Regex.IsMatch(folded, @"\bjpn?\b")) q.Language = "japanese";
        q.Variant = VariantTerms.FirstOrDefault(v => Regex.IsMatch(folded, $@"\b{Regex.Escape(v)}\b"));
        q.Game ??= DetectGame(folded);
        return q;
    }

    public static string? ExtractNumber(string text)
    {
        var code = CodeNumber.Match(text);
        if (code.Success) return code.Value.ToUpperInvariant();
        var slash = SlashNumber.Match(text);
        if (slash.Success) return $"{slash.Groups[1].Value}/{slash.Groups[2].Value}".ToUpperInvariant();
        var hash = HashNumber.Match(text);
        return hash.Success ? hash.Groups[1].Value.ToUpperInvariant() : null;
    }

    private static string? DetectGame(string folded)
    {
        if (Regex.IsMatch(folded, @"\bpokemon\b")) return "pokemon";
        if (Regex.IsMatch(folded, @"\b(mtg|magic the gathering)\b")) return "mtg";
        if (Regex.IsMatch(folded, @"\b(yugioh|yu gi oh)\b")) return "yugioh";
        if (Regex.IsMatch(folded, @"\bone piece\b")) return "one-piece";
        if (Regex.IsMatch(folded, @"\blorcana\b")) return "lorcana";
        return null;
    }

    // ---- SportsCardsPro searches ----

    // Most specific first; the caller tries the next one when a search comes back empty
    public static List<string> SearchQueries(TcgQuery q)
    {
        var queries = new List<string>();
        void Add(params string?[] parts)
        {
            var s = Regex.Replace(string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))), @"\s+", " ").Trim();
            if (s.Length > 0 && !queries.Contains(s, StringComparer.OrdinalIgnoreCase)) queries.Add(s);
        }

        var gameWord = GameWord(q.Game);
        // PriceCharting puts non-English printings in their own sets: "Pokemon Japanese Base Set"
        var japanese = LanguageWord(q.Language);

        if (q.Text != null && q.Name == null)
        {
            // An eBay title: drop the grade and filler, keep the number in SportsCardsPro's form
            var text = GradeRegex.Replace(q.Text, " ");
            text = SlashNumber.Replace(text, m => m.Groups[1].Value);
            text = NoiseWords.Replace(text, " ");
            text = Regex.Replace(text, @"[^\p{L}\p{N}\-\.' ]", " ");
            Add(text);
            return queries;
        }

        var number = SearchNumber(q.Number);
        // SportsCardsPro set names don't repeat the game ("Base Set", not "Pokemon Base Set")
        var set = StripGame(q.Set);
        Add(gameWord, japanese, q.Name, set, number);
        Add(gameWord, japanese, q.Name, number);
        Add(gameWord, japanese, q.Name, set);
        Add(q.Name, number);
        return queries;
    }

    // "4/102" -> "4", "025/198" -> "25", "LOB-EN001" stays (Yu-Gi-Oh! and One Piece codes are searchable)
    public static string? SearchNumber(string? number)
    {
        if (string.IsNullOrWhiteSpace(number)) return null;
        var n = number.Trim().TrimStart('#');
        var slash = n.IndexOf('/');
        if (slash > 0) n = n[..slash];
        n = n.Trim();
        return Regex.IsMatch(n, @"^\d+$") ? n.TrimStart('0').PadLeft(1, '0') : n.ToUpperInvariant();
    }

    // ---- Scoring ----

    public static double Score(TcgQuery q, string productName, string consoleName)
    {
        var product = productName ?? "";
        var console = Fold(consoleName ?? "");

        var bracket = Regex.Match(product, @"\[([^\]]+)\]");
        var candidateVariant = bracket.Success ? Fold(bracket.Groups[1].Value) : null;
        var numberMatch = Regex.Match(product, @"#\s*(\S+)");
        if (!numberMatch.Success) numberMatch = TrailingCode.Match(product);
        var candidateNumber = numberMatch.Success ? numberMatch.Groups[1].Value : null;
        var candidateName = Regex.Replace(numberMatch.Success ? product.Remove(numberMatch.Index, numberMatch.Length) : product,
            @"\[[^\]]*\]", " ");

        // Titles often leave out the game ("Charizard 4/102 Base Set"), but the console name has it
        var queryText = q.Text != null && q.Name == null
            ? q.Text + " " + GameWord(q.Game)
            : string.Join(" ", new[] { GameWord(q.Game), LanguageWord(q.Language), q.Name, q.Set }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        var queryTokens = Tokens(queryText);
        var candidateTokens = Tokens(candidateName + " " + console);
        if (queryTokens.Count == 0 || candidateTokens.Count == 0) return 0;

        // How much of the candidate the card accounts for, and how much of the card the candidate covers.
        // The first matters most: "Base Set 2" has a token "Base Set" doesn't explain.
        var recall = (double)candidateTokens.Count(queryTokens.Contains) / candidateTokens.Count;
        var coverage = (double)queryTokens.Count(candidateTokens.Contains) / queryTokens.Count;
        var score = 0.5 * recall + 0.15 * coverage;

        // The card name has to be there: a set + number match with the wrong name is a different card
        if (q.Name != null)
        {
            var nameTokens = Tokens(q.Name);
            var nameTokensInCandidate = Tokens(candidateName);
            if (nameTokens.Count > 0 && (double)nameTokens.Count(nameTokensInCandidate.Contains) / nameTokens.Count < 0.5)
                score -= 0.3;
        }

        var wanted = NumberKey(q.Number);
        var got = NumberKey(candidateNumber);
        if (wanted != null && got != null)
        {
            if (wanted == got) score += 0.3;
            else if (DigitsOf(wanted) != null && DigitsOf(wanted) == DigitsOf(got)) score += 0.15;
            else score -= 0.4;
        }

        // Variants are priced separately. A [bracket] the card doesn't have is a different printing.
        var cardVariant = q.Variant == null ? null : Fold(q.Variant);
        if (candidateVariant != null)
        {
            // Exact ("Showcase" = [Showcase]) beats partial ("Showcase" vs [Showcase Foil])
            var match = cardVariant == null ? 0 : VariantMatch(cardVariant, candidateVariant);
            if (match == 2) score += 0.1;
            else if (match == 1) score += 0.03;
            else if (q.Rarity != null && VariantMatch(Fold(q.Rarity), candidateVariant) > 0) score += 0.05;
            else score -= 0.3;
        }
        else if (cardVariant != null && IsPricedVariant(cardVariant))
        {
            // Art variants usually have their own collector number (Umbreon VMAX #215 *is* the
            // alternate art), so with the exact number it's only a light penalty. Printings that
            // share the number (1st Edition, Reverse Holo, Foil...) are priced separately.
            var sharesNumber = Regex.IsMatch(cardVariant, @"\b(1st|first|shadowless|reverse|cosmos|ball|foil|etched|staff|prerelease|stamp\w*|error|jumbo)\b");
            score -= !sharesNumber && wanted != null && wanted == got ? 0.05 : 0.15;
        }

        // English, Japanese, Chinese... printings are separate products ("Pokemon Japanese ...")
        var candidateLanguage = Languages.FirstOrDefault(l => Regex.IsMatch(console, $@"\b{l}\b"));
        var cardLanguage = LanguageWord(q.Language)?.ToLowerInvariant();
        if (candidateLanguage != cardLanguage) score -= 0.3;

        return Math.Round(Math.Clamp(score, 0, 1), 3);
    }

    // 2 = same variant, 1 = one is a subset of the other, 0 = different
    private static int VariantMatch(string card, string candidate)
    {
        var a = Tokens(card);
        var b = Tokens(candidate);
        if (a.Count == 0 || b.Count == 0) return 0;
        // "alt art" vs "alternate art", "reverse foil" vs "reverse holo"
        a = a.Select(Synonym).ToHashSet();
        b = b.Select(Synonym).ToHashSet();
        if (a.SetEquals(b)) return 2;
        return b.All(a.Contains) || a.All(b.Contains) ? 1 : 0;
    }

    private static string Synonym(string token) => token switch
    {
        "alt" => "alternate",
        "foil" => "holo",
        "first" => "1st",
        "ed" or "edition" => "edition",
        _ => token
    };

    // Plain "holo" or "unlimited" is usually the standard printing, not a [bracket] variant
    private static bool IsPricedVariant(string variant) =>
        !Regex.IsMatch(variant, @"^(holo|holofoil|non[- ]?holo|unlimited|regular|normal|standard|base|rare holo|holo rare)$");

    public static bool IsJapanese(string? language) => LanguageWord(language) == "Japanese";

    // Languages PriceCharting splits into their own sets (lowercase, as they appear in console names)
    private static readonly string[] Languages = ["japanese", "chinese", "korean", "german", "french", "italian", "spanish", "portuguese"];

    // The console-name word for a non-English card ("Japanese"), null for English or unknown
    public static string? LanguageWord(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;
        var l = Fold(language);
        if (l is "jp" or "jpn" || l.StartsWith("japan")) return "Japanese";
        var match = Languages.FirstOrDefault(x => l.StartsWith(x[..4]));
        return match == null ? null : char.ToUpperInvariant(match[0]) + match[1..];
    }

    // "Blue-Eyes White Dragon LOB-001", "Monkey.D.Luffy OP01-024": the code at the end, no '#'
    private static readonly Regex TrailingCode = new(@"\b([A-Z0-9]{2,6}-[A-Z]{0,2}\d{2,4}[A-Z]?)\s*$", RegexOptions.Compiled);

    // "LOB-EN001" -> "LOB-1", "OP01-024" -> "OP01-24", "4/102" -> "4", "025" -> "25", "TG05/TG30" -> "TG5"
    public static string? NumberKey(string? number)
    {
        if (string.IsNullOrWhiteSpace(number)) return null;
        var n = number.Trim().TrimStart('#').ToUpperInvariant();
        var slash = n.IndexOf('/');
        if (slash > 0) n = n[..slash];
        n = n.Replace(" ", "");

        var code = Regex.Match(n, @"^([A-Z0-9]+)-([A-Z]{0,2})(\d+)([A-Z]?)$");
        if (code.Success) return $"{code.Groups[1].Value}-{TrimZeros(code.Groups[3].Value)}{code.Groups[4].Value}";

        var prefixed = Regex.Match(n, @"^([A-Z]*)(\d+)([A-Z]?)$");
        if (prefixed.Success) return prefixed.Groups[1].Value + TrimZeros(prefixed.Groups[2].Value) + prefixed.Groups[3].Value;
        return n;
    }

    private static string? DigitsOf(string key)
    {
        var m = Regex.Match(key, @"(\d+)[A-Z]?$");
        return m.Success ? TrimZeros(m.Groups[1].Value) : null;
    }

    private static string TrimZeros(string digits) => digits.TrimStart('0').PadLeft(1, '0');

    private static readonly HashSet<string> StopWords = ["the", "of", "and", "a", "an", "tcg", "card", "game"];

    public static HashSet<string> Tokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var folded = Fold(text).Replace("yu gi oh", "yugioh").Replace("&", " ");
        return Regex.Split(folded, @"[^a-z0-9]+")
            .Where(t => t.Length > 0 && !StopWords.Contains(t))
            .ToHashSet();
    }

    private static string? StripGame(string? set)
    {
        if (string.IsNullOrWhiteSpace(set)) return null;
        var s = Regex.Replace(set, @"^\s*(pok[eé]mon|magic(: the gathering)?|mtg|yu-?gi-?oh!?|one piece|(disney )?lorcana)\s*[:\-]?\s*", "", RegexOptions.IgnoreCase);
        return s.Length == 0 ? set : s;
    }

    // Lowercase, accents removed (Pokémon -> pokemon), hyphens in Yu-Gi-Oh! kept as spaces
    public static string Fold(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().ToLowerInvariant().Replace('-', ' ').Trim();
    }
}
