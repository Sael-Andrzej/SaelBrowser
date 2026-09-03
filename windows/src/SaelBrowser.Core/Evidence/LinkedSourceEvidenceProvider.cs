using SaelBrowser.Core.Facts;

namespace SaelBrowser.Core.Evidence;

public sealed class LinkedSourceEvidenceProvider(IPublicSourceVerifier verifier) : IEvidenceProvider
{
    public string Id => "linked-public-sources";

    public async Task<IReadOnlyList<EvidenceItem>> FindAsync(Claim claim, ArticleInput article, CancellationToken cancellationToken)
    {
        var candidates = (article.CitedSources ?? [])
            .Where(source => Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .DistinctBy(source => CanonicalDomain(source.Url)).Take(6).ToArray();
        var results = await Task.WhenAll(candidates.Select(source => verifier.VerifyAsync(source.Url, source.Name, claim, cancellationToken)));
        return results.Where(item => item is not null).Cast<EvidenceItem>().ToArray();
    }

    private static string CanonicalDomain(string url)
    {
        var host = new Uri(url).Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }
}
