using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SaelBrowser.Core.Articles;
using SaelBrowser.Core.Facts;

namespace SaelBrowser.Core.Analysis;

public sealed record ClaimAnalysis(
    int Index, int TotalClaims, Claim Claim, FactResult Result, bool FromCache,
    TimeSpan? FirstEvidenceElapsed, TimeSpan VerdictElapsed);

public sealed record ArticleAnalysisResult(
    ArticleInput Article, IReadOnlyList<Claim> Claims, IReadOnlyList<ClaimAnalysis> Results,
    TimeSpan ExtractionElapsed, TimeSpan ClaimsReadyElapsed, TimeSpan TotalElapsed)
{
    public int TrueCount => Results.Count(item => item.Result.Verdict == FactVerdict.True);
    public int FalseCount => Results.Count(item => item.Result.Verdict == FactVerdict.False);
    public int UnknownCount => Results.Count(item => item.Result.Verdict == FactVerdict.Unknown);
}

public interface IClaimDecomposer
{
    IReadOnlyList<Claim> Decompose(ArticleInput article);
}

public sealed partial class ClaimDecomposer(ClaimExtractor extractor, int maximumClaims = 4) : IClaimDecomposer
{
    public IReadOnlyList<Claim> Decompose(ArticleInput article)
    {
        var titleTokens = Tokens(article.Title);
        var ranked = extractor.Extract(article)
            .Where(claim => claim.Text.Length is >= 20 and <= 320 && !OpinionOrAttributionOnly().IsMatch(claim.Text) && !TopicHeading().IsMatch(claim.Text))
            .Select(claim => (Claim: claim, Score: Score(claim, titleTokens)))
            .OrderByDescending(item => item.Score)
            .ToArray();
        if (article.Domain.Equals("google.com", StringComparison.OrdinalIgnoreCase) && ranked.Length > 0)
            return [ranked[0].Claim];
        var selected = new List<Claim>();
        foreach (var item in ranked)
        {
            if (selected.Any(existing => AreDuplicate(existing.Text, item.Claim.Text))) continue;
            selected.Add(item.Claim with { Priority = Math.Clamp(item.Score, 0, 1) });
            if (selected.Count == maximumClaims) break;
        }
        return selected;
    }

    private static double Score(Claim claim, HashSet<string> titleTokens)
    {
        var tokens = Tokens(claim.Text);
        var overlap = titleTokens.Count == 0 ? 0 : titleTokens.Intersect(tokens).Count() / (double)titleTokens.Count;
        var score = claim.Priority + Math.Min(.3, overlap * .45);
        if (CentralEvidence().IsMatch(claim.Text)) score += .35;
        if (Anecdote().IsMatch(claim.Text)) score -= .25;
        return score;
    }

    public static string NormalizeKey(string text) => string.Join(' ', Word().Matches(text.ToLowerInvariant())
        .Select(match => match.Value).Where(word => word.Length >= 2));

    public static double SemanticSimilarity(string first, string second)
    {
        var left = Tokens(first); var right = Tokens(second);
        if (left.Count == 0 || right.Count == 0) return 0;
        return left.Intersect(right).Count() / (double)Math.Min(left.Count, right.Count);
    }
    private static bool AreDuplicate(string first, string second)
    {
        if (SemanticSimilarity(first, second) >= .60) return true;
        return CrashClaim().IsMatch(first) && CrashClaim().IsMatch(second) &&
            first.Contains("Roswell", StringComparison.OrdinalIgnoreCase) && second.Contains("Roswell", StringComparison.OrdinalIgnoreCase) &&
            first.Contains("1947", StringComparison.Ordinal) && second.Contains("1947", StringComparison.Ordinal);
    }

    private static HashSet<string> Tokens(string value) => Word().Matches(value.ToLowerInvariant())
        .Select(match => match.Value).Where(word => word.Length >= 4 && !StopWords.Contains(word)).ToHashSet();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();
    [GeneratedRegex(@"^\s*(?:moim zdaniem|uważam|sądzę|wydaje mi się|in my opinion|i think)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OpinionOrAttributionOnly();
    [GeneratedRegex(@"\b(?:oficjaln|raport|badani|analiz|wyjaśn|zdement|obalon|dowod|projekt\s+mogul|fact.?check)\w*|\bnie\s+potwierdz\w*", RegexOptions.IgnoreCase)]
    private static partial Regex CentralEvidence();
    [GeneratedRegex(@"\b(?:widział|zeznał|zeznani|opowiadał|relacjonował|wspominał|świadk|relacji|relaksowali|uwagę małżeństwa|jego syn|claimed to see)\w*", RegexOptions.IgnoreCase)]
    private static partial Regex Anecdote();
    [GeneratedRegex(@"\brozbi\w*", RegexOptions.IgnoreCase)]
    private static partial Regex CrashClaim();
    [GeneratedRegex(@"^\s*[\p{L}\p{N} -]+\s+(?:a|i|kontra|wobec)\s+[\p{L}\p{N} -]+[.!:]?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TopicHeading();
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        { "jest", "był", "była", "który", "która", "które", "oraz", "tego", "this", "that", "with", "from", "have", "were" };
}

public sealed class AnalysisResultCache(TimeSpan? ttl = null, int maximumEntries = 100)
{
    private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<(FactResult Result, bool FromCache)> GetOrEvaluateAsync(
        string claim, Func<CancellationToken, Task<FactResult>> evaluate, CancellationToken cancellationToken)
    {
        var key = ClaimDecomposer.NormalizeKey(claim);
        if (TryGet(key, out var cached)) return (cached, true);
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (TryGet(key, out cached)) return (cached, true);
            var result = await evaluate(cancellationToken);
            if (_entries.Count >= maximumEntries)
                foreach (var stale in _entries.OrderBy(item => item.Value.CreatedUtc).Take(Math.Max(1, maximumEntries / 10)))
                    _entries.TryRemove(stale.Key, out _);
            _entries[key] = new Entry(result, DateTimeOffset.UtcNow);
            return (result, false);
        }
        finally { gate.Release(); }
    }

    private bool TryGet(string key, out FactResult result)
    {
        if (_entries.TryGetValue(key, out var entry) && DateTimeOffset.UtcNow - entry.CreatedUtc <= _ttl)
        { result = entry.Result; return true; }
        _entries.TryRemove(key, out _);
        result = null!;
        return false;
    }

    private sealed record Entry(FactResult Result, DateTimeOffset CreatedUtc);
}

public sealed class ArticleAnalysisCoordinator(
    IArticleExtractor extractor, IClaimDecomposer decomposer, FactEngine factEngine,
    AnalysisResultCache cache, int maximumParallelism = 2)
{
    public async Task<ArticleAnalysisResult> AnalyzeAsync(
        string html, string url, Func<ArticleInput, ClaimAnalysis, Task>? resultReady, CancellationToken cancellationToken)
        => await AnalyzeAsync(html, url, null, resultReady, cancellationToken);

    public async Task<ArticleAnalysisResult> AnalyzeAsync(
        string html, string url, Func<ArticleInput, IReadOnlyList<Claim>, Task>? claimsReady,
        Func<ArticleInput, ClaimAnalysis, Task>? resultReady, CancellationToken cancellationToken)
    {
        var total = Stopwatch.StartNew();
        var article = await extractor.ExtractAsync(html, url, cancellationToken);
        var extractionElapsed = total.Elapsed;
        var claims = decomposer.Decompose(article);
        var claimsReadyElapsed = total.Elapsed;
        if (claimsReady is not null) await claimsReady(article, claims);
        using var parallelism = new SemaphoreSlim(maximumParallelism, maximumParallelism);
        var tasks = claims.Select((claim, index) => EvaluateAsync(index, claim)).ToArray();
        var results = await Task.WhenAll(tasks);
        return new ArticleAnalysisResult(article, claims, results.OrderBy(item => item.Index).ToArray(),
            extractionElapsed, claimsReadyElapsed, total.Elapsed);

        async Task<ClaimAnalysis> EvaluateAsync(int index, Claim claim)
        {
            await parallelism.WaitAsync(cancellationToken);
            try
            {
                var clock = Stopwatch.StartNew();
                var claimArticle = article with { Title = claim.Text };
                var (result, fromCache) = await cache.GetOrEvaluateAsync(claim.Text,
                    token => factEngine.EvaluateAsync(claimArticle, token), cancellationToken);
                var firstEvidence = result.EvidenceSets.FirstOrDefault()?.FirstEvidenceElapsed;
                var analysis = new ClaimAnalysis(index, claims.Count, claim, result, fromCache, firstEvidence, clock.Elapsed);
                if (resultReady is not null) await resultReady(article, analysis);
                return analysis;
            }
            finally { parallelism.Release(); }
        }
    }
}
