using SaelBrowser.Core.Articles;
using SaelBrowser.Core.Evidence;
using SaelBrowser.Core.Facts;
using System.Net;
using System.Text;

namespace SaelBrowser.Core.Tests;

public sealed class EvidenceAndFactTests
{
    private static readonly ArticleInput Article = new("Raport wynosi 42 procent", "Raport wynosi 42 procent.", "https://article.example/a", "article.example");
    private static readonly Claim Claim = new("claim", "Raport wynosi 42 procent", true, .9);

    [Fact]
    public async Task NoEvidenceMeansUnknown()
    {
        var result = await Engine([]).EvaluateAsync(Article);
        Assert.Equal(FactVerdict.Unknown, result.Verdict);
        Assert.True(result.Confidence < .5);
    }

    [Fact]
    public async Task ClickbaitNeverMeansFalse()
    {
        var article = Article with { Title = "PILNE! SZOK! MUSISZ TO ZOBACZYĆ 42" };
        var result = await Engine([]).EvaluateAsync(article);
        Assert.NotEqual(FactVerdict.False, result.Verdict);
    }

    [Fact]
    public async Task SingleStrongSourceIsInsufficient()
    {
        var set = await new EvidenceEngine([Provider(Item("a.example", EvidenceStance.Supports))]).EvaluateAsync(Claim, Article, default);
        Assert.False(set.Sufficient);
    }

    [Fact]
    public async Task SameSourceCopiesDoNotPumpConfidence()
    {
        var set = await new EvidenceEngine([Provider(Item("a.example", EvidenceStance.Supports), Item("a.example", EvidenceStance.Supports, "two"))]).EvaluateAsync(Claim, Article, default);
        Assert.False(set.Sufficient);
    }

    [Fact]
    public async Task SecondaryWebPagesAloneCannotProduceVerdict()
    {
        var pages = new[] { "a.example", "b.example", "c.example", "d.example" }
            .Select((domain, index) => Item(domain, EvidenceStance.Supports, index.ToString()) with { SourceType = SourceType.Secondary, Confidence = .9 }).ToArray();
        var result = await Engine([Provider(pages)]).EvaluateAsync(Article);
        Assert.Equal(FactVerdict.Unknown, result.Verdict);
    }

    [Fact]
    public async Task SyndicatedCopiesAcrossDomainsFormOneCluster()
    {
        var first = Item("a.example", EvidenceStance.Supports) with { PrimarySourceId = null, Summary = "Dokument potwierdza raport wynoszący dokładnie 42 procent" };
        var second = Item("b.example", EvidenceStance.Supports) with { PrimarySourceId = null, Summary = "Dokument potwierdza raport wynoszący dokładnie 42 procent" };
        var set = await new EvidenceEngine([Provider(first, second)]).EvaluateAsync(Claim, Article, default);
        Assert.False(set.Sufficient);
    }

    [Fact]
    public async Task TwoIndependentDirectSourcesCanSupportTrue()
    {
        var result = await Engine([Provider(Item("a.example", EvidenceStance.Supports, "official-report"), Item("b.example", EvidenceStance.Supports, "independent-audit"))]).EvaluateAsync(Article);
        Assert.Equal(FactVerdict.True, result.Verdict);
        Assert.True(result.Confidence >= .8);
    }

    [Fact]
    public async Task TwoIndependentDirectSourcesCanRefuteFalse()
    {
        var result = await Engine([Provider(Item("a.example", EvidenceStance.Refutes, "official-correction"), Item("b.example", EvidenceStance.Refutes, "separate-measurement"))]).EvaluateAsync(Article);
        Assert.True(result.Verdict == FactVerdict.False, Describe(result));
        Assert.True(result.Confidence >= .8);
    }

    [Fact]
    public async Task ConflictAlwaysMeansUnknown()
    {
        var result = await Engine([Provider(Item("a.example", EvidenceStance.Supports), Item("b.example", EvidenceStance.Refutes))]).EvaluateAsync(Article);
        Assert.Equal(FactVerdict.Unknown, result.Verdict);
        Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public async Task ArnoldSchwarzeneggerIsDeadIsFalseWithTwoIndependentRefutations()
    {
        var article = new ArticleInput("Arnold Schwarzenegger nie żyje", "Arnold Schwarzenegger nie żyje.", "https://search.example/dead", "search.example");
        var result = await Engine([Provider(
            Item("snopes.com", EvidenceStance.Refutes, "snopes") with { SourceType = SourceType.FactCheck, Confidence = .75 },
            Item("source-a.example", EvidenceStance.Refutes, "official-current-record") with { SourceType = SourceType.Secondary, Confidence = .78 },
            Item("source-b.example", EvidenceStance.Refutes, "independent-recent-interview") with { SourceType = SourceType.Secondary, Confidence = .78 })]).EvaluateAsync(article);
        Assert.True(result.Verdict == FactVerdict.False, Describe(result));
        Assert.True(result.Confidence >= .8);
    }

    [Fact]
    public async Task ArnoldSchwarzeneggerIsAliveIsTrueWithTwoIndependentConfirmations()
    {
        var article = new ArticleInput("Arnold Schwarzenegger żyje", "Arnold Schwarzenegger żyje.", "https://search.example/alive", "search.example");
        var result = await Engine([Provider(
            Item("snopes.com", EvidenceStance.Supports, "snopes") with { SourceType = SourceType.FactCheck, Confidence = .75 },
            Item("source-a.example", EvidenceStance.Supports, "official-current-record") with { SourceType = SourceType.Secondary, Confidence = .78 },
            Item("source-b.example", EvidenceStance.Supports, "independent-recent-interview") with { SourceType = SourceType.Secondary, Confidence = .78 })]).EvaluateAsync(article);
        Assert.True(result.Verdict == FactVerdict.True, Describe(result));
        Assert.True(result.Confidence >= .8);
    }

    [Theory]
    [InlineData("Arnold Schwarzenegger nie żyje", "Actor Arnold Schwarzenegger has passed away. — False", "Actor Arnold Schwarzenegger has passed away of a heart attack.", EvidenceStance.Refutes)]
    [InlineData("Arnold Schwarzenegger żyje", "Actor Arnold Schwarzenegger has passed away. — False", "Actor Arnold Schwarzenegger has passed away of a heart attack.", EvidenceStance.Supports)]
    public void StructuredFactCheckRatingRespectsClaimPolarity(string query, string snippet, string reviewedClaim, EvidenceStance expected) =>
        Assert.Equal(expected, EvidenceSemantics.FactCheckStance(query, reviewedClaim, snippet));

    [Theory]
    [InlineData("The Earth is flat", "The Earth is flat.", "Deadly Disinfo — Tragically False", EvidenceStance.Refutes)]
    [InlineData("The Earth is round", "The Earth is flat.", "The Earth is not flat — False", EvidenceStance.Supports)]
    [InlineData("COVID-19 vaccines contain microchips", "COVID-19 vaccines contain microchips.", "Vaccines Don't Have Tracking Devices — False", EvidenceStance.Refutes)]
    public void StructuredRatingsSupportMultiplePredicateFamilies(string query, string reviewedClaim, string snippet, EvidenceStance expected) =>
        Assert.Equal(expected, EvidenceSemantics.FactCheckStance(query, reviewedClaim, snippet));

    [Theory]
    [InlineData("Nie ma wiarygodnych dowodów na wzrost liczby nowotworów po szczepieniu przeciw COVID-19.", "Szczepienia przeciw COVID-19 powodują nowotwory.", "Demagog — Fałsz", EvidenceStance.Supports)]
    [InlineData("Brak dowodów na to, że szczepienia przeciw COVID-19 powodują nowotwory.", "Szczepienia przeciw COVID-19 powodują nowotwory.", "Ocena — Prawda", EvidenceStance.Refutes)]
    [InlineData("Nie ma dowodów na to, że szczepienia powodują nowotwory.", "Szczepionki zawierają grafen.", "Ocena — Fałsz", EvidenceStance.Unknown)]
    [InlineData("Nie ma dowodów na to, że 5G powoduje nowotwory.", "5G powoduje bóle głowy.", "Ocena — Fałsz", EvidenceStance.Unknown)]
    [InlineData("Nie ma dowodów na to, że 5G powoduje nowotwory.", "5G powoduje nowotwory.", "Artykuł omawia temat", EvidenceStance.Unknown)]
    public void PolishEvidenceNegationRequiresMatchingExplicitFactCheck(string query, string reviewedClaim, string snippet, EvidenceStance expected) =>
        Assert.Equal(expected, EvidenceSemantics.FactCheckStance(query, reviewedClaim, snippet));

    [Fact]
    public void PolishEvidenceNegationProducesPositiveClaimCoreWithoutMalformedDeletion()
    {
        var variants = EvidenceSemantics.QueryVariants("Nie ma wiarygodnych dowodów na wzrost liczby nowotworów po szczepieniu przeciw COVID-19.");
        Assert.Contains(variants, value => value.Contains("szczepienie przeciw COVID-19 powoduje wzrost liczby nowotwory", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(variants, value => value.StartsWith("fact check szczepienie", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(variants, value => value.StartsWith("ma wiarygodnych dowodów", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PolishNoEvidenceRelationProducesPositiveFiveGCore()
    {
        var variants = EvidenceSemantics.QueryVariants("Nie ma też dowodów naukowych, które potwierdzałyby związek między promieniowaniem elektromagnetycznym używanym w sieci 5G a występowaniem nowotworów.");
        Assert.Contains(variants, value => value.Contains("promieniowanie elektromagnetyczne używane w sieci 5G powoduje nowotwory", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("5G powoduje nowotwory", variants);
    }

    [Theory]
    [InlineData("Szczepionki i 5G powodują nowotwory", "Szczepienia przeciw COVID-19 powodują nowotwory.", "Demagog — Fałsz")]
    [InlineData("Szczepionki i 5G powodują nowotwory", "Technologia 5G powoduje nowotwory.", "Demagog — Fałsz")]
    public void CancerCausationFactChecksMatchExplicitCompoundTitle(string query, string reviewedClaim, string snippet) =>
        Assert.Equal(EvidenceStance.Refutes, EvidenceSemantics.FactCheckStance(query, reviewedClaim, snippet));

    [Fact]
    public void PolishSearchClaimGetsCleanEnglishProviderVariant()
    {
        var variants = EvidenceSemantics.QueryVariants("zmarł Schwarzenegger - Szukaj w Google");
        Assert.Contains("Schwarzenegger is dead", variants);
        Assert.DoesNotContain(variants, value => value.Contains("Szukaj w Google", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Arnold Schwarzenegger nie żyje", "Arnold Schwarzenegger żyje i pracuje nad nowym filmem.", EvidenceStance.Refutes)]
    [InlineData("Arnold Schwarzenegger żyje", "Arnold Schwarzenegger żyje i pracuje nad nowym filmem.", EvidenceStance.Supports)]
    [InlineData("Arnold Schwarzenegger żyje", "Artykuł opisuje karierę Arnolda Schwarzeneggera.", EvidenceStance.Unknown)]
    public void LinkedSourceNeedsAnExplicitStatement(string claim, string sourceText, EvidenceStance expected) =>
        Assert.Equal(expected, EvidenceSemantics.ExplicitTextStance(claim, sourceText));

    [Theory]
    [InlineData("The Earth is flat", "Flat Earth is an archaic and scientifically disproven conception.")]
    [InlineData("The Earth is flat", "The belief that the Earth is flat was widespread in the myth.")]
    [InlineData("The Eiffel Tower is in Paris", "Images show the Eiffel Tower in Paris on fire.")]
    public void TopicMentionIsNotAnExplicitStance(string claim, string sourceText) =>
        Assert.Equal(EvidenceStance.Unknown, EvidenceSemantics.ExplicitTextStance(claim, sourceText));

    [Fact]
    public async Task WeakSecondaryNoiseDoesNotCreateConflictAgainstStrongIndependentEvidence()
    {
        var items = new[]
        {
            Item("a.example", EvidenceStance.Supports, "official-record"),
            Item("b.example", EvidenceStance.Supports, "independent-audit"),
            Item("noise.example", EvidenceStance.Refutes, "weak-topic-mention") with { SourceType = SourceType.Secondary, Confidence = .6 }
        };
        var result = await Engine([Provider(items)]).EvaluateAsync(Article);
        Assert.Equal(FactVerdict.True, result.Verdict);
    }

    [Fact]
    public void CopiedWireStoriesSharePrimarySourceCluster()
    {
        var first = PublicSourceVerifier.PrimarySource("Reuters reported that the ministry published the result.", "copy-a.example");
        var second = PublicSourceVerifier.PrimarySource("Według Reuters ministerstwo opublikowało wynik.", "copy-b.example");
        Assert.Equal("wire:reuters", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void IndependentPublishersKeepDifferentPrimarySourceIds()
    {
        Assert.NotEqual(PublicSourceVerifier.PrimarySource("Independent report A", "a.example"),
            PublicSourceVerifier.PrimarySource("Independent report B", "b.example"));
    }

    [Fact]
    public async Task DiscoveryCandidatesMustBeFetchedAndExplicitlyVerified()
    {
        var claim = new Claim("claim", "Arnold Schwarzenegger żyje", true, 1);
        var verified = Item("independent.example", EvidenceStance.Supports, "verified");
        var provider = new DiscoveryEvidenceProvider([new FakeDiscovery()], new FakeVerifier(verified));
        var items = await provider.FindAsync(claim, Article, default);
        Assert.Single(items);
        Assert.Equal(EvidenceStance.Supports, items[0].Stance);
    }

    [Fact]
    public async Task EncyclopediaTopicPageIsNotTreatedAsDirectEvidence()
    {
        var verifier = new PublicSourceVerifier(new HttpClient(), new ArticleExtractor());
        var item = await verifier.VerifyAsync("https://en.wikipedia.org/wiki/Flat_Earth", "Wikipedia",
            new Claim("claim", "The Earth is flat", true, 1), default);
        Assert.Null(item);
    }

    [Fact]
    public async Task PageEvidenceIsDiscarded()
    {
        var page = Item("page.example", EvidenceStance.Supports) with { Origin = EvidenceOrigin.PageContent };
        var set = await new EvidenceEngine([Provider(page, page with { Url = "https://other.example/x", Domain = "other.example" })]).EvaluateAsync(Claim, Article, default);
        Assert.Empty(set.Items);
        Assert.False(set.Sufficient);
    }

    [Fact]
    public async Task ProviderFailureAndTimeoutRemainUnknown()
    {
        var failures = new IEvidenceProvider[] { new ThrowingProvider(), new TimeoutProvider() };
        var set = await new EvidenceEngine(failures, TimeSpan.FromMilliseconds(20)).EvaluateAsync(Claim, Article, default);
        Assert.False(set.Sufficient);
        Assert.Equal(2, set.ProviderErrors.Count);
    }

    [Fact]
    public async Task TransientPrimaryFailureRetriesOnceWithoutCallingFallbackAfterSuccess()
    {
        var handler = new RecordingHandler((request, call) => call == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : EvidenceResponse(request, []));
        var provider = new RemoteEvidenceProvider(new HttpClient(handler), "https://primary.example", "https://fallback.example");
        await provider.FindDetailedAsync(Claim, Article, default);
        Assert.Equal(EvidenceSemantics.QueryVariants(Claim.Text).Length + 1, handler.Requests.Count);
        Assert.All(handler.Requests, uri => Assert.Equal("primary.example", uri.Host));
    }

    [Fact]
    public async Task SuccessfulEmptyResponseDoesNotRetryOrCallFallback()
    {
        var handler = new RecordingHandler((request, _) => EvidenceResponse(request, []));
        var provider = new RemoteEvidenceProvider(new HttpClient(handler), "https://primary.example", "https://fallback.example");
        await provider.FindDetailedAsync(Claim, Article, default);
        Assert.Equal(EvidenceSemantics.QueryVariants(Claim.Text).Length, handler.Requests.Count);
        Assert.All(handler.Requests, uri => Assert.Equal("primary.example", uri.Host));
    }

    [Fact]
    public async Task RateLimitDoesNotRetryOrCallFallback()
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var provider = new RemoteEvidenceProvider(new HttpClient(handler), "https://primary.example", "https://fallback.example");
        await provider.FindDetailedAsync(Claim, Article, default);
        Assert.Equal(EvidenceSemantics.QueryVariants(Claim.Text).Length, handler.Requests.Count);
    }

    [Fact]
    public async Task BackendWarningsArePreservedAsDiagnostics()
    {
        var handler = new RecordingHandler((request, _) => EvidenceResponse(request, [], "brave-search: unavailable"));
        var provider = new RemoteEvidenceProvider(new HttpClient(handler), "https://primary.example");
        var result = await provider.FindDetailedAsync(Claim, Article, default);
        Assert.Contains(result.Diagnostics, item => item.Stage == "provider-warning" && item.Reason == "brave-search: unavailable");
    }

    [Fact]
    public async Task SamePublisherArticlesDoNotReplaceTwoIndependentClusters()
    {
        var first = Item("demagog.org.pl", EvidenceStance.Refutes, "article-one") with { PrimarySourceId = "publisher:demagog" };
        var second = Item("demagog.org.pl", EvidenceStance.Refutes, "article-two") with { PrimarySourceId = "publisher:demagog" };
        var set = await new EvidenceEngine([Provider(first, second)]).EvaluateAsync(Claim, Article, default);
        Assert.Single(set.Clusters!);
        Assert.False(set.Sufficient);
    }

    [Fact]
    public void HistoricalQueriesContainEnglishEntityDateCompactAndPolarizedVariants()
    {
        var variants = EvidenceSemantics.QueryVariants("W Roswell w 1947 roku rozbił się statek obcych");
        Assert.Contains(variants, value => value.Contains("Roswell", StringComparison.OrdinalIgnoreCase) && value.Contains("1947"));
        Assert.Contains(variants, value => value.Contains("alien spacecraft", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(variants, value => value.StartsWith("fact check", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(variants, value => value.Contains("no evidence", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("W Roswell rozbił się statek obcych", EvidenceStance.Refutes)]
    [InlineData("Władze ustaliły, że obiekt z Roswell był związany z Projektem Mogul i balonem meteorologicznym", EvidenceStance.Supports)]
    [InlineData("Roswell jest dokładnie obalonym twierdzeniem o UFO", EvidenceStance.Supports)]
    public void OfficialRoswellTextClassifiesExplicitHistoricalClaims(string claim, EvidenceStance expected)
    {
        const string official = "The Air Force research found no evidence that the Roswell incident was a UFO event. The recovered debris came from the balloon-borne Project Mogul research project.";
        Assert.Equal(expected, EvidenceSemantics.ExplicitTextStance(claim, official));
    }

    [Fact]
    public async Task EmptyDiscoveryHasAuditableZeroCandidateReason()
    {
        var provider = new DiscoveryEvidenceProvider([new EmptyDiscovery()], new FakeVerifier(Item("unused.example", EvidenceStance.Unknown)));
        var result = await provider.FindDetailedAsync(Claim, Article, default);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, item => !item.Accepted && item.Reason == "0 candidates");
    }

    [Fact]
    public async Task RoswellWitnessDiscoveryIncludesIndependentTranscriptCandidate()
    {
        var sources = await new InstitutionalHistoryDiscovery().SearchAsync(
            new Claim("history", "W materiale UFOs are Real Jesse Marcel mówił o szczątkach", true, 1), default);
        Assert.Contains(sources, item => item.Url.Contains("transcripts.cnn.com", StringComparison.OrdinalIgnoreCase));
    }

    private static FactEngine Engine(IReadOnlyList<IEvidenceProvider> providers) => new(new ClaimExtractor(), new ClickbaitAnalyzer(), new EvidenceEngine(providers));
    private static string Describe(FactResult result) => string.Join(" | ", result.EvidenceSets.Select(set => $"{set.Message} s={set.SupportConfidence} r={set.RefuteConfidence} clusters={set.Clusters?.Count}"));
    private static FakeProvider Provider(params EvidenceItem[] items) => new(items);
    private static EvidenceItem Item(string domain, EvidenceStance stance, string suffix = "one") =>
        new("claim", "Jednoznaczny dokument " + suffix, $"https://{domain}/{suffix}", domain, domain, "2026-08-01",
            SourceType.PrimaryDocument, stance, 1, EvidenceOrigin.VerifiedDatabase, domain + suffix, true);

    private sealed class FakeProvider(EvidenceItem[] items) : IEvidenceProvider
    {
        public string Id => "fake";
        public Task<IReadOnlyList<EvidenceItem>> FindAsync(Claim claim, ArticleInput article, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EvidenceItem>>(items.Select(i => i with { ClaimId = claim.Id }).ToArray());
    }
    private sealed class ThrowingProvider : IEvidenceProvider
    {
        public string Id => "throw";
        public Task<IReadOnlyList<EvidenceItem>> FindAsync(Claim claim, ArticleInput article, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
    private sealed class TimeoutProvider : IEvidenceProvider
    {
        public string Id => "timeout";
        public async Task<IReadOnlyList<EvidenceItem>> FindAsync(Claim claim, ArticleInput article, CancellationToken cancellationToken)
        { await Task.Delay(500, cancellationToken); return []; }
    }
    private sealed class FakeDiscovery : ISourceDiscovery
    {
        public string Id => "fake-discovery";
        public Task<IReadOnlyList<SourceCandidate>> SearchAsync(Claim claim, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SourceCandidate>>([new("https://independent.example/report", "Independent")]);
    }
    private sealed class EmptyDiscovery : ISourceDiscovery
    {
        public string Id => "empty-discovery";
        public Task<IReadOnlyList<SourceCandidate>> SearchAsync(Claim claim, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SourceCandidate>>([]);
    }
    private sealed class FakeVerifier(EvidenceItem item) : IPublicSourceVerifier
    {
        public Task<EvidenceItem?> VerifyAsync(string url, string publisher, Claim claim, CancellationToken cancellationToken) =>
            Task.FromResult<EvidenceItem?>(item with { ClaimId = claim.Id });
    }

    private static HttpResponseMessage EvidenceResponse(HttpRequestMessage request, string[] evidence, params string[] warnings)
    {
        var query = System.Text.Json.JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement.GetProperty("claim").GetString();
        var json = System.Text.Json.JsonSerializer.Serialize(new { query, evidence, warnings, cacheHit = false });
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request, Requests.Count));
        }
    }
}
