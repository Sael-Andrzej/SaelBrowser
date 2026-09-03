using SaelBrowser.Core.Articles;
using SaelBrowser.Core.Evidence;
using SaelBrowser.Core.Facts;

namespace SaelBrowser.Core.Tests;

public sealed class ArticleAndClickbaitTests
{
    private readonly SaelTitleRewriter _titles = new(new ClickbaitAnalyzer());

    [Fact]
    public void ClickbaitBecomesFactualSaelTitle()
    {
        var rewritten = _titles.Rewrite("SZOK! Nie uwierzysz, co stało się w Warszawie", "Rada Warszawy przyjęła budżet na 2026 rok. Za uchwałą głosowało 32 radnych.");
        Assert.Equal("Rada Warszawy przyjęła budżet na 2026 rok.", rewritten);
    }

    [Fact]
    public async Task ClickbaitWithoutEvidenceGetsNeutralTitleAndUnknownVerdict()
    {
        const string content = "Minister opublikował raport w Warszawie 1 września 2026 roku.";
        var rewritten = _titles.Rewrite("PILNE: MINISTER OPUBLIKOWAŁ RAPORT — MUSISZ TO ZOBACZYĆ", content);
        Assert.Equal("Minister opublikował raport", rewritten);
        var clickbait = new ClickbaitAnalyzer().Analyze(rewritten, content);
        Assert.True(clickbait.Score < .2);
        var engine = new FactEngine(new ClaimExtractor(), new ClickbaitAnalyzer(), new EvidenceEngine([]));
        var result = await engine.EvaluateAsync(new ArticleInput(rewritten, content, "https://example.test/article", "example.test"));
        Assert.Equal(FactVerdict.Unknown, result.Verdict);
    }
    [Fact]
    public async Task ExtractorKeepsArticleAndRemovesNavigationCommentsAndAds()
    {
        const string html = """
          <html><head><meta property="og:title" content="Rzeczywisty tytuł"><meta name="author" content="Jan Kowalski"></head>
          <body><nav>MENU</nav><article><h1>Inny tytuł</h1><figure><figcaption>Podpis ilustracji z 1893 r.</figcaption></figure><p>Główna treść artykułu zawiera ważną informację i liczbę 42.</p>
          <a href="https://source.example/report">Raport</a><div class="comments">Komentarz</div><div class="advertisement">Reklama</div></article><footer>STOPKA</footer></body></html>
          """;
        var result = await new ArticleExtractor().ExtractAsync(html, "https://news.example/item");
        Assert.Equal("Rzeczywisty tytuł", result.Title);
        Assert.Equal("Jan Kowalski", result.Author);
        Assert.Contains("Główna treść", result.Content);
        Assert.DoesNotContain("Komentarz", result.Content);
        Assert.DoesNotContain("Reklama", result.Content);
        Assert.DoesNotContain("Podpis ilustracji", result.Content);
        Assert.Single(result.CitedSources!);
    }

    [Fact]
    public async Task ExtractorPreservesReadableDomBlockBoundaries()
    {
        const string html = "<article><p>First report was published.[8]</p><h2>Historical context</h2><p>Second report was published.</p></article>";
        var result = await new ArticleExtractor().ExtractAsync(html, "https://example.test/article");
        Assert.Equal(new[] { "First report was published.[8]", "Historical context.", "Second report was published." },
            result.Content.Split(Environment.NewLine));
    }

    [Fact]
    public async Task GoogleSearchUsesTheQueryAsPrimaryClaimTitle()
    {
        const string html = "<html><head><title>zmarł schwarzenegger - Szukaj w Google</title></head><body><form><input name='q' value='zmarł schwarzenegger'></form><main>Wyniki wyszukiwania <a href='https://snopes.com/fact-check/arnold'>Snopes</a></main></body></html>";
        var article = await new ArticleExtractor().ExtractAsync(html, "https://www.google.com/search?q=zmar%C5%82+schwarzenegger");
        Assert.Equal("zmarł schwarzenegger", article.Title);
        Assert.Contains(article.CitedSources!, source => source.Url.Contains("snopes.com", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(FactVerdict.True, "PRAWDA")]
    [InlineData(FactVerdict.False, "FAŁSZ")]
    [InlineData(FactVerdict.Unknown, "NIE WIEM")]
    public void VerdictLabelsArePolish(FactVerdict verdict, string expected) =>
        Assert.Equal(expected, FactPresentation.VerdictLabel(verdict));

    [Fact] public async Task EmptyAndBrokenContentIsSafe()
    {
        var empty = await new ArticleExtractor().ExtractAsync("", "not a url");
        var broken = await new ArticleExtractor().ExtractAsync("<article><p>", "https://example.com");
        Assert.Empty(empty.Content);
        Assert.NotNull(broken);
    }

    [Fact]
    public void ClickbaitIsScoredButIsNotAVerdict()
    {
        var result = new ClickbaitAnalyzer().Analyze("PILNE! MUSISZ TO ZOBACZYĆ — SZOK", "Zwykła treść opisuje inne zagadnienie.");
        Assert.True(result.Score >= .6);
        Assert.NotEmpty(result.Reasons);
    }

    [Fact] public void OrdinaryTitleHasLowClickbait() => Assert.True(new ClickbaitAnalyzer().Analyze("Minister opublikował raport za 2025 rok", "Minister opublikował raport za 2025 rok.").Score < .5);

    [Fact]
    public void ClaimsRequireFactualSignal()
    {
        var extractor = new ClaimExtractor();
        Assert.NotEmpty(extractor.Extract(new("Raport wynosi 42 procent", "Raport wynosi 42 procent.", "https://x.example", "x.example")));
        Assert.Empty(extractor.Extract(new("Moim zdaniem piękny dzień", "Cudowny i spokojny opis.", "https://x.example", "x.example")));
    }

    [Theory]
    [InlineData("Szczepienia przeciw COVID-19 a nowotwory.")]
    [InlineData("5G i zdrowie.")]
    [InlineData("Szczepionki kontra rak.")]
    public void ExtractorRejectsTopicHeadingsWithoutPredicate(string heading)
    {
        var claims = new ClaimExtractor().Extract(new(heading, heading, "https://x.example", "x.example"));
        Assert.Empty(claims);
    }

    [Theory]
    [InlineData("Szczepienia przeciw COVID-19 powodują nowotwory.")]
    [InlineData("Nie ma dowodów na związek 5G z nowotworami.")]
    public void ExtractorKeepsCompletePositiveAndNegativeClaims(string statement)
    {
        var claims = new ClaimExtractor().Extract(new(statement, statement, "https://x.example", "x.example"));
        Assert.Contains(claims, claim => claim.Text == statement);
    }

    [Fact]
    public void WikipediaFootnotesSeparateIndependentSentencesAndStayWithPreviousSentence()
    {
        var content = "The Earth was flat.[8] This myth was created later.[9]";
        var claims = new ClaimExtractor().Extract(new("", content, "https://en.wikipedia.org/wiki/Flat_Earth", "wikipedia.org"));
        Assert.Contains(claims, claim => claim.Text == "The Earth was flat.[8]");
        Assert.Contains(claims, claim => claim.Text == "This myth was created later.[9]");
        Assert.DoesNotContain(claims, claim => claim.Text.Contains("[8] This", StringComparison.Ordinal));
    }

    [Fact]
    public void HistoricalCircaAbbreviationDoesNotCreateAnEraSuffixFragment()
    {
        const string sentence = "Anaximenes of Miletus (c. 550–500 BC) thought that the Earth was flat.[41]";
        var claims = new ClaimExtractor().Extract(new("", sentence, "https://en.wikipedia.org/wiki/Flat_Earth", "wikipedia.org"));
        Assert.Single(claims);
        Assert.Equal(sentence, claims[0].Text);
        Assert.DoesNotContain(claims, claim => claim.Text.StartsWith("500 BC)", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Dr. John Smith reported a value of 3.14% on 07.06.2023.")]
    [InlineData("Prof. J. Smith reported that version 1.2.3 is stable.")]
    [InlineData("The U.S. report was published by J. Smith.")]
    [InlineData("The result was described e.g. in the official report.")]
    public void AbbreviationsInitialsDecimalsDatesAndVersionsRemainSingleSentences(string sentence)
    {
        var claims = new ClaimExtractor().Extract(new("", sentence, "https://example.test", "example.test"));
        Assert.Single(claims);
        Assert.Equal(sentence, claims[0].Text);
    }

    [Theory]
    [InlineData("500 BC) thought that the Earth was flat.[41]")]
    [InlineData("[41] claimed that the Earth was flat.")]
    [InlineData(") maintained that the Earth was flat.")]
    public void ObviousSubjectlessContinuationIsRejected(string fragment)
    {
        var claims = new ClaimExtractor().Extract(new("", fragment, "https://example.test", "example.test"));
        Assert.Empty(claims);
    }

    [Fact]
    public void FactualArticleTitleHasPriorityOverNumericMetadata()
    {
        var article = new ArticleInput(
            "Ziemia jest płaska i nieskończenie wielka? Fałszywa teoria",
            "Opublikowano 07.06.2023 o 12:25. Materiał ma 14 minut i 3 sekundy.",
            "https://example.com/fact-check", "example.com");
        var claims = new ClaimExtractor().Extract(article);
        Assert.NotEmpty(claims);
        Assert.Equal(article.Title, claims[0].Text);
        Assert.Equal(1, claims[0].Priority);
    }
}
