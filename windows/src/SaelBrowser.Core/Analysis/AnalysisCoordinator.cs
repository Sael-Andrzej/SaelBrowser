using SaelBrowser.Core.Articles;
using SaelBrowser.Core.Facts;

namespace SaelBrowser.Core.Analysis;

public sealed class AnalysisCoordinator(IArticleExtractor extractor, FactEngine factEngine)
{
    public async Task<FactResult> AnalyzeAsync(string html, string url, CancellationToken cancellationToken) =>
        (await AnalyzeWithTraceAsync(html, url, cancellationToken)).Result;

    public async Task<AnalysisOutcome> AnalyzeWithTraceAsync(string html, string url, CancellationToken cancellationToken)
    {
        var article = await extractor.ExtractAsync(html, url, cancellationToken);
        var result = await factEngine.EvaluateAsync(article, cancellationToken);
        return new AnalysisOutcome(article, result);
    }
}

public sealed record AnalysisOutcome(ArticleInput Article, FactResult Result);
