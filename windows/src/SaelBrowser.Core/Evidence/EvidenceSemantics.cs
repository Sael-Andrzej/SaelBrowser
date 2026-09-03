using System.Text.RegularExpressions;
using SaelBrowser.Core.Facts;

namespace SaelBrowser.Core.Evidence;

public static partial class EvidenceSemantics
{
    public static string[] QueryVariants(string claim)
    {
        var normalized = Normalize(claim.Replace("- Szukaj w Google", "", StringComparison.OrdinalIgnoreCase));
        var variants = new List<string> { normalized };
        var deadFirst = DeadFirst().Match(normalized);
        var deadLast = DeadLast().Match(normalized);
        var alive = Alive().Match(normalized);
        if (deadFirst.Success) variants.Add($"{deadFirst.Groups[1].Value} is dead");
        else if (deadLast.Success) variants.Add($"{deadLast.Groups[1].Value} is dead");
        else if (alive.Success) variants.Add($"{alive.Groups[1].Value} is alive");
        var english = TranslateHistoricalTerms(normalized);
        if (!english.Equals(normalized, StringComparison.OrdinalIgnoreCase)) variants.Add(english);
        if (normalized.Contains("Roswell", StringComparison.OrdinalIgnoreCase))
        {
            var year = Year().Matches(normalized).Select(match => match.Value).FirstOrDefault() ?? "1947";
            variants.Add($"Roswell incident {year}");
        }
        var entities = ProperNamesAndDates(normalized);
        if (entities.Length >= 8) variants.Add(entities);
        var compact = Compact(normalized);
        if (compact.Length >= 8) variants.Add(compact);
        variants.Add("fact check " + english);
        variants.Add(PolarizedAlternative(english));
        return variants.Select(Normalize).Where(value => value.Length >= 8)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(7).ToArray();
    }

    private static string TranslateHistoricalTerms(string value)
    {
        var translated = value;
        foreach (var pair in HistoricalTranslations)
            translated = Regex.Replace(translated, Regex.Escape(pair.Key), pair.Value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return translated;
    }

    private static string ProperNamesAndDates(string value)
    {
        var words = Word().Matches(value).Select(match => match.Value).ToArray();
        return string.Join(' ', words.Where((word, index) =>
            Year().IsMatch(word) || (word.Length >= 3 && char.IsUpper(word[0]) && index > 0) ||
            HistoricalTerms.Contains(word, StringComparer.OrdinalIgnoreCase)));
    }

    private static string Compact(string value) => string.Join(' ', Tokens(value)
        .Where(token => !QueryStopWords.Contains(token)).Take(12));

    private static string PolarizedAlternative(string value)
    {
        if (Regex.IsMatch(value, @"\b(?:no|not|without|brak|nie)\b", RegexOptions.IgnoreCase))
            return Regex.Replace(value, @"\b(?:no|not|without|brak|nie)\b", "", RegexOptions.IgnoreCase);
        return value + " no evidence";
    }

    public static EvidenceStance FactCheckStance(string query, string reviewedClaim, string snippet)
    {
        var rating = Rating(snippet);
        if (!SameSubject(query, reviewedClaim)) return EvidenceStance.Unknown;
        if (rating == 0)
        {
            var requestedPolarity = Polarity(query); var snippetPolarity = Polarity(snippet);
            return requestedPolarity != 0 && snippetPolarity != 0
                ? requestedPolarity == snippetPolarity ? EvidenceStance.Supports : EvidenceStance.Refutes
                : EvidenceStance.Unknown;
        }
        var relation = Polarity(query) is var queryPolarity && queryPolarity != 0 && Polarity(reviewedClaim) is var reviewedPolarity && reviewedPolarity != 0
            ? queryPolarity == reviewedPolarity ? 1 : -1
            : Similarity(query, reviewedClaim) >= .65 ? 1 : 0;
        if (relation == 0) return EvidenceStance.Unknown;
        return rating * relation > 0 ? EvidenceStance.Supports : EvidenceStance.Refutes;
    }

    public static EvidenceStance ExplicitTextStance(string claim, string text)
    {
        var historical = HistoricalStance(claim, text);
        if (historical != EvidenceStance.Unknown) return historical;
        if (!SameSubject(claim, text)) return EvidenceStance.Unknown;
        var claimPolarity = Polarity(claim);
        var sentences = Sentence().Split(Normalize(text)).Where(value => SameSubject(claim, value));
        foreach (var sentence in sentences)
        {
            if (ReportedClaim().IsMatch(sentence)) continue;
            if (ContainsPhrase(sentence, claim)) return EvidenceStance.Supports;
            if (claimPolarity == 0) continue;
            var sourcePolarity = Polarity(sentence);
            if (sourcePolarity != 0 && ExplicitRelation(claim, sentence))
                return sourcePolarity == claimPolarity ? EvidenceStance.Supports : EvidenceStance.Refutes;
        }
        return EvidenceStance.Unknown;
    }

    private static EvidenceStance HistoricalStance(string claim, string text)
    {
        var requested = Normalize(claim).ToLowerInvariant();
        var source = Normalize(text).ToLowerInvariant();
        var historicalTopic = requested.Contains("roswell") || requested.Contains("mogul") || requested.Contains("ufos are real");
        if (!historicalTopic || !source.Contains("roswell")) return EvidenceStance.Unknown;
        var sourceBalloon = Regex.IsMatch(source, @"\b(?:project\s+mogul|weather balloon|balloon-borne|balloon device|high[- ]altitude balloon|materials recovered.{0,40}balloon|balon meteorologiczny|balonowy projekt)\b", RegexOptions.IgnoreCase);
        var sourceRejectsAlien = Regex.IsMatch(source, @"\b(?:no (?:information|evidence|records?).{0,80}(?:ufo|alien|extraterrestrial)|did not (?:locate|prove)|not prove|no indication.{0,60}(?:ufo|extraterrestrial)|claims? of .{0,40} alien bodies.{0,40}(?:most likely|appear))\b", RegexOptions.IgnoreCase);
        var claimBalloon = Regex.IsMatch(requested, @"\b(?:projekt\w*\s+mogul|project\s+mogul|balon\w* meteorologiczn\w*|weather balloon)\b", RegexOptions.IgnoreCase);
        var claimDebunked = Regex.IsMatch(requested, @"\b(?:obalon|zdement|debunk|brak (?:potwierdzonych )?dowod)\w*", RegexOptions.IgnoreCase);
        var claimAlienCrash = Regex.IsMatch(requested, @"\b(?:statek.{0,15}obcych|alien spacecraft|pojazd pozaziemski|extraterrestrial vehicle)\b", RegexOptions.IgnoreCase);
        var claimMarcel = requested.Contains("marcel") || requested.Contains("ufos are real");
        if (claimMarcel)
        {
            if (!source.Contains("marcel")) return EvidenceStance.Unknown;
            if (Regex.IsMatch(source, @"\b(?:not (?:remains of )?(?:a )?(?:weather )?balloon|wasn['’]t (?:a )?(?:weather )?balloon|nie by[łl](?:o|y)? to balon)\b", RegexOptions.IgnoreCase)) return EvidenceStance.Supports;
            return EvidenceStance.Unknown;
        }
        if (claimBalloon && sourceBalloon) return EvidenceStance.Supports;
        if (claimDebunked && (sourceRejectsAlien || sourceBalloon)) return EvidenceStance.Supports;
        if (claimAlienCrash && (sourceRejectsAlien || sourceBalloon)) return EvidenceStance.Refutes;
        return EvidenceStance.Unknown;
    }

    private static bool ExplicitRelation(string claim, string sentence)
    {
        var subject = Tokens(claim).Where(token => !PredicateWords.Contains(token) && token is not "the" and not "is" and not "are")
            .OrderByDescending(token => token.Length).FirstOrDefault();
        if (subject is null) return false;
        var normalized = Normalize(sentence).ToLowerInvariant();
        var index = normalized.IndexOf(subject, StringComparison.Ordinal);
        if (index < 0) return false;
        var local = normalized.Substring(index, Math.Min(140, normalized.Length - index));
        return CopularPredicate().IsMatch(local);
    }

    private static bool ContainsPhrase(string text, string phrase)
    {
        static string Comparable(string value) => string.Join(' ', Word().Matches(value.ToLowerInvariant()).Select(match => match.Value));
        var expected = Comparable(phrase); var actual = Comparable(text);
        return expected.Length >= 8 && actual.Contains(expected, StringComparison.Ordinal);
    }

    private static int Rating(string snippet)
    {
        var value = Normalize(snippet).ToLowerInvariant();
        if (FalseRating().IsMatch(value)) return -1;
        if (TrueRating().IsMatch(value)) return 1;
        return 0;
    }
    private static int Polarity(string value)
    {
        value = Normalize(value).ToLowerInvariant();
        if (NegatedDeath().IsMatch(value)) return -1;
        if (NotAlive().IsMatch(value)) return 1;
        var death = DeathWords().IsMatch(value); var alive = AliveWords().IsMatch(value);
        if (death != alive) return death ? 1 : -1;
        if (NegatedFlat().IsMatch(value)) return -2;
        var flat = FlatWords().IsMatch(value); var round = RoundWords().IsMatch(value);
        if (flat != round) return flat ? 2 : -2;
        if (NoMicrochips().IsMatch(value)) return -3;
        if (Microchips().IsMatch(value)) return 3;
        return 0;
    }
    private static bool SameSubject(string first, string second)
    {
        var left = Tokens(first).Where(token => !PredicateWords.Contains(token)).ToHashSet();
        var right = Tokens(second).ToHashSet();
        return left.Any(token => token.Length >= 5 && right.Contains(token));
    }
    private static double Similarity(string first, string second)
    {
        var left = Tokens(first).ToHashSet(); var right = Tokens(second).ToHashSet();
        return left.Count == 0 || right.Count == 0 ? 0 : left.Intersect(right).Count() / (double)left.Union(right).Count();
    }
    private static IEnumerable<string> Tokens(string value) => Word().Matches(Normalize(value).ToLowerInvariant()).Select(match => match.Value).Where(value => value.Length >= 3);
    private static string Normalize(string value) => string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static readonly HashSet<string> PredicateWords = ["zmarł", "zmarła", "żyje", "dead", "died", "alive", "passed", "away", "nie", "flat", "round", "spherical", "microchips", "contains", "contain"];
    private static readonly Dictionary<string, string> HistoricalTranslations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["statek obcych"] = "alien spacecraft", ["statku obcych"] = "alien spacecraft",
        ["obiekt latający"] = "flying object", ["obiektu latającego"] = "flying object",
        ["balon meteorologiczny"] = "weather balloon", ["balonu meteorologicznego"] = "weather balloon",
        ["szczątki"] = "debris", ["rozbił się"] = "crashed", ["rozbicie"] = "crash",
        ["brak dowodów"] = "no evidence", ["nie znaleziono"] = "not found",
        ["świadek"] = "witness", ["świadka"] = "witness", ["relacja"] = "testimony",
        ["dobrze zbadane"] = "well investigated", ["obalone"] = "debunked"
    };
    private static readonly string[] HistoricalTerms = ["Roswell", "Mogul", "UFO", "Marcel", "alien", "balloon", "debris"];
    private static readonly HashSet<string> QueryStopWords = ["który", "która", "które", "oraz", "został", "została", "jest", "jako", "przez", "this", "that", "with", "from", "were", "was", "been", "have"];

    [GeneratedRegex(@"^\s*zmarł\s+(.+)$", RegexOptions.IgnoreCase)] private static partial Regex DeadFirst();
    [GeneratedRegex(@"^\s*(.+?)\s+(?:nie żyje|zmarł|zmarła)\s*$", RegexOptions.IgnoreCase)] private static partial Regex DeadLast();
    [GeneratedRegex(@"^\s*(.+?)\s+żyje\s*$", RegexOptions.IgnoreCase)] private static partial Regex Alive();
    [GeneratedRegex(@"\b(?:false|fałsz|fake|incorrect|mostly false|pants on fire|tragically false|not true|unsupported|fictional|ai-generated)\b", RegexOptions.IgnoreCase)] private static partial Regex FalseRating();
    [GeneratedRegex(@"\b(?:true|prawda|correct|mostly true|accurate)\b", RegexOptions.IgnoreCase)] private static partial Regex TrueRating();
    [GeneratedRegex(@"\b(?:żyje|is alive|remains alive|still alive|nie zmarł|nie zmarła|nie umarł|nie umarła|did not die|has not died)\b", RegexOptions.IgnoreCase)] private static partial Regex AliveWords();
    [GeneratedRegex(@"\b(?:nie zmarł|nie zmarła|nie umarł|nie umarła|did not die|has not died|is not dead)\b", RegexOptions.IgnoreCase)] private static partial Regex NegatedDeath();
    [GeneratedRegex(@"\b(?:nie żyje|is not alive)\b", RegexOptions.IgnoreCase)] private static partial Regex NotAlive();
    [GeneratedRegex(@"\b(?:zmarł|zmarła|umarł|umarła|nie żyje|is dead|has died|died|passed away)\b", RegexOptions.IgnoreCase)] private static partial Regex DeathWords();
    [GeneratedRegex(@"\b(?:not flat|isn't flat|nie jest płaska|nie jest plaska)\b", RegexOptions.IgnoreCase)] private static partial Regex NegatedFlat();
    [GeneratedRegex(@"\b(?:round|spherical|sphere|kulista|okrągła|okragla)\b", RegexOptions.IgnoreCase)] private static partial Regex RoundWords();
    [GeneratedRegex(@"\b(?:flat|płaska|plaska)\b", RegexOptions.IgnoreCase)] private static partial Regex FlatWords();
    [GeneratedRegex(@"\b(?:do not contain microchips|does not contain microchips|don't contain microchips|without microchips|nie zawierają mikrochipów|nie zawieraja mikrochipow)\b", RegexOptions.IgnoreCase)] private static partial Regex NoMicrochips();
    [GeneratedRegex(@"\b(?:contain microchips|contains microchips|microchip implants|zawierają mikrochipy|zawieraja mikrochipy)\b", RegexOptions.IgnoreCase)] private static partial Regex Microchips();
    [GeneratedRegex(@"\b(?:is alive|is dead|has died|passed away|żyje|nie żyje|zmarł|zmarła|is flat|is not flat|is round|is spherical|contains? microchips?|nie zawiera(?:ją)? mikrochipów|jest płaska|jest kulista)\b", RegexOptions.IgnoreCase)] private static partial Regex CopularPredicate();
    [GeneratedRegex(@"\b(?:claim|claims|claimed|belief|believes|believed|theory|myth|misconception|rumou?r|hoax|alleged|proponents?|idea|disproven|twierdzi|twierdzenie|pogląd|teoria|mit|plotka|rzekomo)\b", RegexOptions.IgnoreCase)] private static partial Regex ReportedClaim();
    [GeneratedRegex(@"(?<=[.!?])\s+")] private static partial Regex Sentence();
    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();
    [GeneratedRegex(@"^(?:18|19|20)\d{2}$")]
    private static partial Regex Year();
}
