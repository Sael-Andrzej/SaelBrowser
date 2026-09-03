using SaelBrowser.Core.Analysis;
using SaelBrowser.Core.Articles;
using SaelBrowser.Core.Evidence;
using SaelBrowser.Core.Facts;

namespace SaelBrowser.Core.Tests;

public sealed class ArticleAnalysisTests
{
    [Fact]
    public void DecomposerRejectsQuestionOpinionAndSemanticDuplicateAndLimitsClaims()
    {
        var article = new ArticleInput("Co naprawdę wykazał raport?", """
            Moim zdaniem raport jest bardzo ciekawy.
            Ziemia jest okrągła według pomiarów geodezyjnych.
            Według pomiarów geodezyjnych planeta Ziemia jest okrągła.
            Księżyc jest wykonany z zielonego sera.
            Dokument ma numer katalogowy 42.
            Autor opublikował raport w 2026 roku.
            """, "https://example.test/article", "example.test");
        var claims = new ClaimDecomposer(new ClaimExtractor(), 3).Decompose(article);
        Assert.InRange(claims.Count, 2, 3);
        Assert.DoesNotContain(claims, claim => claim.Text.StartsWith("Moim zdaniem", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, claims.Count(claim => claim.Text.Contains("okrągła", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task EachClaimGetsIndependentVerdictAndResultsAreProgressive()
    {
        var provider = new DeterministicEvidenceProvider();
        var coordinator = Coordinator(provider, new AnalysisResultCache(TimeSpan.FromMinutes(5)));
        var ready = new List<ClaimAnalysis>();
        var result = await coordinator.AnalyzeAsync(ArticleHtml, "https://example.test/article",
            (_, item) => { lock (ready) ready.Add(item); return Task.CompletedTask; }, default);
        Assert.Equal(result.Results.Count, ready.Count);
        Assert.Contains(result.Results, item => item.Claim.Text.Contains("Ziemia") && item.Result.Verdict == FactVerdict.True);
        Assert.Contains(result.Results, item => item.Claim.Text.Contains("Księżyc") && item.Result.Verdict == FactVerdict.False);
        Assert.Contains(result.Results, item => item.Claim.Text.Contains("katalogowy") && item.Result.Verdict == FactVerdict.Unknown);
    }

    [Fact]
    public async Task CacheAvoidsRepeatingEvidenceRequestsForSameClaims()
    {
        var provider = new DeterministicEvidenceProvider();
        var coordinator = Coordinator(provider, new AnalysisResultCache(TimeSpan.FromMinutes(5)));
        var first = await coordinator.AnalyzeAsync(ArticleHtml, "https://example.test/article", null, default);
        var callsAfterFirst = provider.Calls;
        var second = await coordinator.AnalyzeAsync(ArticleHtml, "https://example.test/article", null, default);
        Assert.True(callsAfterFirst > 0);
        Assert.Equal(callsAfterFirst, provider.Calls);
        Assert.All(second.Results, item => Assert.True(item.FromCache));
        Assert.Equal(first.Results.Select(item => item.Result.Verdict), second.Results.Select(item => item.Result.Verdict));
    }

    [Fact]
    public async Task NavigationCancellationStopsOutstandingClaimWork()
    {
        var coordinator = Coordinator(new DelayedEvidenceProvider(), new AnalysisResultCache());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.AnalyzeAsync(ArticleHtml, "https://example.test/article", null, cancellation.Token));
    }

    private static ArticleAnalysisCoordinator Coordinator(IEvidenceProvider provider, AnalysisResultCache cache) =>
        new(new ArticleExtractor(), new ClaimDecomposer(new ClaimExtractor(), 4),
            new FactEngine(new ClaimExtractor(), new ClickbaitAnalyzer(), new EvidenceEngine([provider], TimeSpan.FromSeconds(2))), cache, 2);

    private const string ArticleHtml = """
      <html><head><title>Co naprawdę wykazał raport?</title></head><body><article>
      <p>Ziemia jest okrągła według pomiarów geodezyjnych.</p>
      <p>Księżyc jest wykonany z zielonego sera.</p>
      <p>Dokument ma numer katalogowy 42.</p>
      </article></body></html>
      """;

    private sealed class DeterministicEvidenceProvider : IEvidenceProvider
    {
        public string Id => "raw-test-evidence";
        public int Calls { get; private set; }
        public Task<IReadOnlyList<EvidenceItem>> FindAsync(Claim claim, ArticleInput article, CancellationToken cancellationToken)
        {
            Calls++;
            var stance = claim.Text.Contains("Ziemia", StringComparison.OrdinalIgnoreCase) ? EvidenceStance.Supports :
                claim.Text.Contains("Księżyc", StringComparison.OrdinalIgnoreCase) ? EvidenceStance.Refutes : EvidenceStance.Unknown;
            if (stance == EvidenceStance.Unknown) return Task.FromResult<IReadOnlyList<EvidenceItem>>([]);
            return Task.FromResult<IReadOnlyList<EvidenceItem>>([
                Item(claim, "fact-a.example", stance, "A"), Item(claim, "fact-b.example", stance, "B")]);
        }
        private static EvidenceItem Item(Claim claim, string domain, EvidenceStance stance, string suffix) =>
            new(claim.Id, suffix == "A" ? "Pomiary geodezyjne i obserwacje horyzontu dają jednoznaczny wynik." : "Niezależna analiza zdjęć satelitarnych potwierdza odmienny zestaw danych.", $"https://{domain}/check/{suffix}", domain, domain,
                "2026-01-01", SourceType.FactCheck, stance, .75, EvidenceOrigin.VerifiedDatabase, $"fact:{domain}:{suffix}", true);
    }

    private sealed class DelayedEvidenceProvider : IEvidenceProvider
    {
        public string Id => "delayed";
        public async Task<IReadOnlyList<EvidenceItem>> FindAsync(Claim claim, ArticleInput article, CancellationToken cancellationToken)
        { await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken); return []; }
    }
}
