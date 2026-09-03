using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SaelBrowser.Core.Facts;

public sealed partial class ClickbaitAnalyzer
{
    public ClickbaitResult Analyze(string title, string content)
    {
        if (string.IsNullOrWhiteSpace(title)) return new(0, []);
        var reasons = new List<string>();
        var score = 0d;
        var letters = new string(title.Where(char.IsLetter).ToArray());
        if (letters.Length >= 12 && letters.Count(char.IsUpper) / (double)letters.Length >= .7)
        { score += .3; reasons.Add("nadmierne użycie wielkich liter"); }
        if (Sensational().IsMatch(title)) { score += .35; reasons.Add("sensacyjne sformułowanie"); }
        if (title.Contains('?') && SuggestiveQuestion().IsMatch(title))
        { score += .2; reasons.Add("pytanie sugerujące sensacyjną odpowiedź"); }
        var titleTokens = Tokens(title);
        var contentTokens = Tokens(content.Take(4000));
        if (titleTokens.Count >= 3 && titleTokens.Intersect(contentTokens).Count() / (double)titleTokens.Count < .25)
        { score += .25; reasons.Add("słaba zgodność tytułu z treścią"); }
        return new(Math.Min(1, score), reasons);
    }

    private static HashSet<string> Tokens(IEnumerable<char> value) =>
        Word().Matches(new string(value.ToArray()).ToLowerInvariant()).Select(m => m.Value).Where(v => v.Length > 3).ToHashSet();
    [GeneratedRegex(@"\b(PILNE|SZOK|MUSISZ TO ZOBACZYĆ|NIE UWIERZYSZ|BREAKING)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Sensational();
    [GeneratedRegex(@"\b(czy|dlaczego|jak to możliwe|co ukrywają)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SuggestiveQuestion();
    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();
}

public sealed partial class ClaimExtractor
{
    public IReadOnlyList<Claim> Extract(ArticleInput article)
    {
        var title = Normalize(article.Title);
        return new[] { (Text: title, IsTitle: true, Index: -1) }
            .Concat(SplitSentences(article.Content).Select((text, index) => (Text: text, IsTitle: false, Index: index)))
            .Select(item => (Text: Normalize(item.Text), item.IsTitle, item.Index))
            .Where(item => item.Text.Length is >= 8 and <= 500 && !IsQuestion(item.Text) && !CallToAction().IsMatch(item.Text) &&
                HasAssertivePredicate(item.Text) && !IncompleteLeadingFragment().IsMatch(item.Text))
            .DistinctBy(item => item.Text).Take(30)
            .Select(item => new Claim(Id(item.Text), item.Text, Factual().IsMatch(item.Text), Priority(item.Text, item.IsTitle, title, item.Index), article.PublishedAt))
            .Where(c => c.IsFactual).OrderByDescending(c => c.Priority).ToArray();
    }

    private static double Priority(string text, bool isTitle, string title, int index)
    {
        var score = .55 + (isTitle ? .45 : 0) + (Number().IsMatch(text) ? .08 : 0) + (Date().IsMatch(text) ? .08 : 0);
        if (!isTitle)
        {
            score += Math.Min(.3, TokenOverlap(title, text) * .45);
            score += Math.Max(0, .08 - Math.Max(0, index) * .01);
            if (MetaClaim().IsMatch(text)) score -= .25;
        }
        return Math.Clamp(score, 0, 1);
    }
    private static double TokenOverlap(string title, string text)
    {
        static HashSet<string> Words(string value) => Word().Matches(value.ToLowerInvariant()).Select(match => match.Value)
            .Where(word => word.Length >= 4 && !StopWords.Contains(word)).ToHashSet();
        var titleWords = Words(title); var textWords = Words(text);
        return titleWords.Count == 0 ? 0 : titleWords.Intersect(textWords).Count() / (double)titleWords.Count;
    }
    private static bool IsQuestion(string text) => text.TrimEnd().EndsWith('?') || QuestionStart().IsMatch(text);
    private static bool HasAssertivePredicate(string text) => AssertivePredicate().IsMatch(text) || QuantitativeAssertion().IsMatch(text);
    private static IEnumerable<string> SplitSentences(string text)
    {
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is '\r' or '\n')
            {
                if (index > start) yield return text[start..index];
                while (index + 1 < text.Length && text[index + 1] is '\r' or '\n') index++;
                start = index + 1;
                continue;
            }
            if (text[index] is not ('.' or '!' or '?') || text[index] == '.' && IsProtectedPeriod(text, index)) continue;
            var end = index + 1;
            while (end < text.Length && text[end] == '[')
            {
                var close = text.IndexOf(']', end + 1);
                if (close < 0 || !text.AsSpan(end + 1, close - end - 1).ToString().All(char.IsDigit)) break;
                end = close + 1;
            }
            if (end < text.Length && !char.IsWhiteSpace(text[end])) continue;
            yield return text[start..end];
            while (end < text.Length && char.IsWhiteSpace(text[end]) && text[end] is not ('\r' or '\n')) end++;
            start = end;
            index = end - 1;
        }
        if (start < text.Length) yield return text[start..];
    }

    private static bool IsProtectedPeriod(string text, int index)
    {
        if (index > 0 && index + 1 < text.Length && char.IsDigit(text[index - 1]) && char.IsDigit(text[index + 1])) return true;
        var begin = index - 1;
        while (begin >= 0 && (char.IsLetter(text[begin]) || text[begin] == '.')) begin--;
        var token = text[(begin + 1)..(index + 1)];
        if (ProtectedAbbreviations.Contains(token)) return true;
        return token.Length == 2 && char.IsUpper(token[0]);
    }
    private static string Id(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToLowerInvariant())))[..16].ToLowerInvariant();
    private static string Normalize(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    [GeneratedRegex(@"(?<!\w)\d+(?:[.,]\d+)?")]
    private static partial Regex Number();
    [GeneratedRegex(@"\b(?:\d{1,2}[.-]\d{1,2}[.-]\d{2,4}|\d{4}-\d{2}-\d{2})\b")]
    private static partial Regex Date();
    [GeneratedRegex(@"\b(wynosi|ogłosił|ogłosiła|opublikował|opublikowała|podpisał|zmarł|zmarła|żyje|urodził|jest|był|była|ma|posiada|stwierdził|reported|announced|described|maintained|thought|created|is|was|has|died|dead|alive)\b|(?<!\w)\d+(?:[.,]\d+)?", RegexOptions.IgnoreCase)]
    private static partial Regex Factual();
    [GeneratedRegex(@"\b(?:wynosi|ogłosił|ogłosiła|opublikował|opublikowała|podpisał|zmarł|zmarła|żyje|urodził|jest|są|był|była|ma|mają|posiada|stwierdził|powoduje|powodują|wywołuje|wywołują|zwiększa|zwiększają|reported|announced|described|maintained|thought|created|is|are|was|has|causes?|increases?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AssertivePredicate();
    [GeneratedRegex(@"(?<!\w)\d+(?:[.,]\d+)?\s*(?:%|proc\.?|procent|mln|mld|lat|rok|roku|osób|osob|przypadk)", RegexOptions.IgnoreCase)]
    private static partial Regex QuantitativeAssertion();
    [GeneratedRegex(@"^\s*(?:czy|co|kto|gdzie|kiedy|dlaczego|jak|which|what|who|where|when|why|how)\b", RegexOptions.IgnoreCase)]
    private static partial Regex QuestionStart();
    [GeneratedRegex(@"\b(?:twierdzenie|teoria|spekulacj|plotk|pogłosk|is claimed|allegedly)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MetaClaim();
    [GeneratedRegex(@"\b(?:kliknij|zobacz|czytaj\s+także|czytaj\s+dalej|tvp\s+vod|dowiedz\s+się)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CallToAction();
    [GeneratedRegex(@"^\s*(?:(?:\[\d+\]|\))\s*|\d{1,4}(?:\s*[–-]\s*\d{1,4})?\s*(?:BC|BCE|AD|CE)\s*\))", RegexOptions.IgnoreCase)]
    private static partial Regex IncompleteLeadingFragment();
    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        { "czym", "naprawdę", "który", "która", "które", "oraz", "tego", "this", "that", "with", "from" };
    private static readonly HashSet<string> ProtectedAbbreviations = new(StringComparer.OrdinalIgnoreCase)
        { "c.", "ca.", "e.g.", "i.e.", "Dr.", "Mr.", "Mrs.", "Prof.", "St.", "vs.", "etc.", "np.", "tj.", "prof.", "dr.", "r.", "U.S.", "U.K." };
}
