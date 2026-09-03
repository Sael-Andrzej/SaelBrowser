using System.Net;
using System.Text;
using SaelBrowser.Core.Analysis;
using SaelBrowser.Core.Articles;
using SaelBrowser.Core.Evidence;
using SaelBrowser.Core.Facts;

namespace SaelBrowser.Core.Tests;

public sealed class WindowsPipelineIntegrationTests
{
    [Fact]
    public async Task QuestionArticleFlowsFromBodyClaimToUnknownAndNeutralDomTitle()
    {
        const string html = """
          <html><head><meta property='og:title' content='Roswell 1947. Czym naprawdę był rozbity obiekt? Balon czy UFO?'></head>
          <body><article><h1>Co wydarzyło się w Roswell? Tajemnica latającego dysku</h1>
          <p>Incydent w Roswell jest znany jako dokładnie obalone twierdzenie o UFO.</p>
          <p>Według niektórych relacji 2 lipca 1947 roku w pobliżu Roswell miał rozbić się statek należący do obcych.</p>
          <p>Władze stwierdziły, że był to balon związany z wojskowym Projektem Mogul.</p></article></body></html>
          """;
        var coordinator = new AnalysisCoordinator(new ArticleExtractor(),
            new FactEngine(new ClaimExtractor(), new ClickbaitAnalyzer(), new EvidenceEngine([])));

        var outcome = await coordinator.AnalyzeWithTraceAsync(html, "https://news.example/roswell", default);
        var neutral = new SaelTitleRewriter(new ClickbaitAnalyzer()).Rewrite(
            outcome.Article.Title, outcome.Article.Content, outcome.Result.Claims[0].Text, outcome.Result.Verdict);

        Assert.StartsWith("Według niektórych relacji 2 lipca 1947", outcome.Result.Claims[0].Text);
        Assert.Equal(FactVerdict.Unknown, outcome.Result.Verdict);
        Assert.DoesNotContain('?', neutral);
        Assert.Contains("Roswell", neutral);
        Assert.Equal("Roswell 1947: obiekt opisywany jako balon lub UFO", neutral);
        Assert.NotEqual(outcome.Article.Title, neutral);
    }

    [Fact]
    public void ShortButExplicitFactualClaimIsNotDiscarded()
    {
        var claims = new ClaimExtractor().Extract(new ArticleInput(
            "The Earth is flat", "", "https://www.google.com/search?q=The+Earth+is+flat", "google.com"));

        Assert.Single(claims);
        Assert.Equal("The Earth is flat", claims[0].Text);
    }

    [Fact]
    public async Task GoogleSearchUrlFlowsThroughEvidenceFactEngineAndUiAsFalse()
    {
        const string url = "https://www.google.com/search?q=The+Earth+is+flat";
        const string html = "<html><head><title>Google</title></head><body><form><input name='q'></form><main>Search results</main></body></html>";
        using var http = new HttpClient(new FactCheckResponseHandler());
        var coordinator = new AnalysisCoordinator(
            new ArticleExtractor(),
            new FactEngine(new ClaimExtractor(), new ClickbaitAnalyzer(),
                new EvidenceEngine([new RemoteEvidenceProvider(http, "https://evidence.test")])));

        var outcome = await coordinator.AnalyzeWithTraceAsync(html, url, default);

        Assert.Equal("The Earth is flat", outcome.Article.Title);
        Assert.Equal("The Earth is flat", outcome.Result.Claims[0].Text);
        Assert.Equal(3, outcome.Result.EvidenceSets[0].Clusters?.Count);
        Assert.All(outcome.Result.EvidenceSets[0].Items, item => Assert.Equal(EvidenceStance.Refutes, item.Stance));
        Assert.Equal(FactVerdict.False, outcome.Result.Verdict);
        Assert.True(outcome.Result.Confidence >= .8);
        Assert.StartsWith("FAŁSZ • 96%", FactPresentation.VerdictButtonText(outcome.Result));
    }

    [Fact]
    public async Task GoogleQueryInUrlWinsWhenSerializedInputContainsStaleValue()
    {
        var article = await new ArticleExtractor().ExtractAsync(
            "<html><head><title>Google</title></head><body><input name='q' value='stare wyszukiwanie'><main>Results</main></body></html>",
            "https://www.google.com/search?q=Elon+Musk+tweeted+the+world+is+flat");

        Assert.Equal("Elon Musk tweeted the world is flat", article.Title);
    }

    private sealed class FactCheckResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string json = """
            {
              "query":"The Earth is flat",
              "evidence":[
                {"claim":"The Earth is flat","snippet":"Full Fact review concludes the flat Earth claim is false.","url":"https://fullfact.org/online/flat-earth/","domain":"fullfact.org","publisher":"Full Fact","publishedAt":"2025-01-01","sourceType":"FACT_CHECK","stance":"UNKNOWN","provenance":"GOOGLE_FACT_CHECK","provider":"google-fact-check","providerConfidence":0.75},
                {"claim":"The Earth is flat","snippet":"AAP FactCheck rated this assertion incorrect and false.","url":"https://aap.com.au/factcheck/flat-earth/","domain":"aap.com.au","publisher":"AAP","publishedAt":"2025-01-02","sourceType":"FACT_CHECK","stance":"UNKNOWN","provenance":"GOOGLE_FACT_CHECK","provider":"google-fact-check","providerConfidence":0.75},
                {"claim":"The Earth is flat","snippet":"USA Today verification found the statement false, based on scientific observations.","url":"https://usatoday.com/factcheck/flat-earth/","domain":"usatoday.com","publisher":"USA Today","publishedAt":"2025-01-03","sourceType":"FACT_CHECK","stance":"UNKNOWN","provenance":"GOOGLE_FACT_CHECK","provider":"google-fact-check","providerConfidence":0.75}
              ],
              "warnings":[],"cacheHit":false
            }
            """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
