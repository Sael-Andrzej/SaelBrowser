using System.Text.RegularExpressions;

namespace SaelBrowser.Core.Facts;

public sealed partial class SaelTitleRewriter(ClickbaitAnalyzer clickbait)
{
    public string Rewrite(string title, string nearbyContent, string? mainClaim = null, FactVerdict verdict = FactVerdict.Unknown)
    {
        var original = Normalize(title);
        if (original.Length == 0 || clickbait.Analyze(original, nearbyContent).Score < .2) return original;

        var stripped = Normalize(HookPrefix().Replace(original, ""));
        stripped = Normalize(HookSuffix().Replace(stripped, ""));
        var letters = new string(stripped.Where(char.IsLetter).ToArray());
        if (letters.Length >= 12 && letters.All(character => !char.IsLetter(character) || char.IsUpper(character)))
            stripped = char.ToUpperInvariant(stripped[0]) + stripped[1..].ToLowerInvariant();

        var questionSummary = NeutralQuestionSummary(original, nearbyContent);
        if (questionSummary is not null) return questionSummary;

        var candidates = SentenceBoundary().Split(Normalize(nearbyContent))
            .Select(Normalize)
            .Where(IsUsefulSentence)
            .ToArray();
        var candidate = (string.IsNullOrWhiteSpace(mainClaim) ? null : Normalize(mainClaim))
            ?? candidates.FirstOrDefault(HasNeutralEvidenceLanguage)
            ?? candidates.FirstOrDefault();

        if ((IsVague(stripped) || original.Contains('?')) && candidate is not null) return Trim(candidate);
        return Trim(stripped.Length >= 18 ? stripped : candidate ?? original);
    }

    private static bool IsUsefulSentence(string value) =>
        value.Length is >= 25 and <= 220 &&
        FactualSignal().IsMatch(value) &&
        !CallToAction().IsMatch(value);

    private static bool IsVague(string value) => value.Length < 18 || Vague().IsMatch(value);
    private static bool HasNeutralEvidenceLanguage(string value) => NeutralEvidence().IsMatch(value);
    private static string? NeutralQuestionSummary(string title, string content)
    {
        var topic = TopicAndYear().Match(title);
        var alternatives = Alternatives().Match(title);
        if (!topic.Success || !alternatives.Success) return null;
        var first = ChoiceCase(Normalize(alternatives.Groups["first"].Value));
        var second = ChoiceCase(Normalize(alternatives.Groups["second"].Value));
        if (!content.Contains(first, StringComparison.OrdinalIgnoreCase) || !content.Contains(second, StringComparison.OrdinalIgnoreCase)) return null;
        var prefix = $"{Normalize(topic.Groups["topic"].Value)} {topic.Groups["year"].Value}";
        var noun = title.Contains("obiekt", StringComparison.OrdinalIgnoreCase) ? "obiekt opisywany jako " : "relacje: ";
        return Trim($"{prefix}: {noun}{first} lub {second}");
    }
    private static string ChoiceCase(string value)
    {
        var letters = value.Where(char.IsLetter).ToArray();
        return letters.Length is > 1 and <= 6 && letters.All(char.IsUpper) ? value : value.ToLowerInvariant();
    }
    private static string Trim(string value) => value.Length <= 180 ? value : value[..177].TrimEnd() + "…";
    private static string Normalize(string value) => string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '-', '–', '—', ':');

    [GeneratedRegex(@"^\s*(?:PILNE|SZOK|WIDEO|VIDEO|GALERIA|ZDJĘCIA|RELACJA|LIVE|BREAKING)\s*[:!\-–—]*\s*", RegexOptions.IgnoreCase)]
    private static partial Regex HookPrefix();
    [GeneratedRegex(@"\s*(?:MUSISZ TO ZOBACZYĆ|NIE UWIERZYSZ|ZOBACZ(?:CIE)?(?:,?\s+CO\s+SIĘ\s+STAŁO)?|KLIKNIJ,?\s+ABY\s+SIĘ\s+DOWIEDZIEĆ)[.!?…]*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex HookSuffix();
    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceBoundary();
    [GeneratedRegex(@"\b(?:jest|są|był|była|będzie|ma|miał|wynosi|ogłosił|ogłosiła|opublikował|opublikowała|zmarł|wygrał|przegrał|rozpoczął|zakończył|is|was|has|announced|reported)\b|\d", RegexOptions.IgnoreCase)]
    private static partial Regex FactualSignal();
    [GeneratedRegex(@"\b(?:kliknij|zobacz|sprawdź|dowiedz się|czytaj dalej)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CallToAction();
    [GeneratedRegex(@"\b(?:to|tego|takiego|co się stało|nie uwierzysz|musisz)\b|[?…]$", RegexOptions.IgnoreCase)]
    private static partial Regex Vague();
    [GeneratedRegex(@"\b(?:obalon|nie potwierdz|brak dowod|wyjaśn|balon|projekt\s+mogul|false|debunk|no evidence)\w*", RegexOptions.IgnoreCase)]
    private static partial Regex NeutralEvidence();
    [GeneratedRegex(@"^(?<topic>[\p{L}\p{N} .'-]{2,60}?)\s+(?<year>(?:19|20)\d{2})\s*[.\-–—:]", RegexOptions.IgnoreCase)]
    private static partial Regex TopicAndYear();
    [GeneratedRegex(@"(?<first>[\p{L}][\p{L} -]{1,35})\s+czy\s+(?<second>[\p{L}][\p{L} -]{1,35})\?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Alternatives();
}
