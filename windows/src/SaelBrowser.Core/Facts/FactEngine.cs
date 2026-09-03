using SaelBrowser.Core.Evidence;

namespace SaelBrowser.Core.Facts;

public sealed class FactEngine(ClaimExtractor claims, ClickbaitAnalyzer clickbait, EvidenceEngine evidence)
{
    public async Task<FactResult> EvaluateAsync(ArticleInput article, CancellationToken cancellationToken = default)
    {
        var clickbaitResult = clickbait.Analyze(article.Title, article.Content);
        var extracted = claims.Extract(article);
        var sets = new List<EvidenceSet>();
        foreach (var claim in extracted.Take(1))
            sets.Add(await evidence.EvaluateAsync(claim, article, cancellationToken));

        FactVerdict verdict;
        double confidence;
        string rationale;
        var primary = sets.FirstOrDefault();
        if (primary?.Conflict == true)
        { verdict = FactVerdict.Unknown; confidence = 0; rationale = "Źródła są sprzeczne. Wyniku nie można uczciwie rozstrzygnąć."; }
        else if (primary is { Sufficient: true } && primary.SupportConfidence > primary.RefuteConfidence)
        { verdict = FactVerdict.True; confidence = primary.SupportConfidence; rationale = "Niezależne dowody zewnętrzne przekroczyły bezpieczny próg potwierdzenia głównego twierdzenia."; }
        else if (primary is { Sufficient: true } && primary.RefuteConfidence > primary.SupportConfidence)
        { verdict = FactVerdict.False; confidence = primary.RefuteConfidence; rationale = "Niezależne dowody zewnętrzne przekroczyły bezpieczny próg obalenia głównego twierdzenia."; }
        else
        { verdict = FactVerdict.Unknown; confidence = Math.Min(.49, sets.SelectMany(s => new[] { s.SupportConfidence, s.RefuteConfidence }).DefaultIfEmpty(0).Max()); rationale = "Brak niezależnego, jednoznacznego dowodu pozwalającego uczciwie potwierdzić lub obalić twierdzenie."; }

        var sources = sets.SelectMany(s => s.Items).Select(i => new FactSource(i.Publisher, i.Url, i.PublicationDate))
            .Concat(article.CitedSources ?? []).DistinctBy(s => s.Url).ToArray();
        return new(verdict, confidence, rationale, clickbaitResult, extracted, sets, sources);
    }
}
