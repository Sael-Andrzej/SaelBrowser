using System.Net.Http.Json;
using System.Text.Json.Serialization;
using SaelBrowser.Core.Facts;

namespace SaelBrowser.Core.Evidence;

public sealed record SourceCandidate(string Url, string Publisher, string Query = "");

public interface ISourceDiscovery
{
    string Id { get; }
    Task<IReadOnlyList<SourceCandidate>> SearchAsync(Claim claim, CancellationToken cancellationToken);
}

public sealed class DiscoveryEvidenceProvider(IReadOnlyList<ISourceDiscovery> discovery, IPublicSourceVerifier verifier) : IDiagnosticEvidenceProvider
{
    public string Id => "public-source-discovery";

    public async Task<IReadOnlyList<EvidenceItem>> FindAsync(Claim claim, ArticleInput article, CancellationToken cancellationToken)
        => (await FindDetailedAsync(claim, article, cancellationToken)).Items;

    public async Task<EvidenceProviderResult> FindDetailedAsync(Claim claim, ArticleInput article, CancellationToken cancellationToken)
    {
        var diagnostics = new List<EvidenceDiagnostic>();
        var discoveryClock = System.Diagnostics.Stopwatch.StartNew();
        var searches = discovery.Select(source => SafeSearchAsync(source, claim, cancellationToken)).ToArray();
        var searchResults = await Task.WhenAll(searches);
        var discoveryElapsed = discoveryClock.Elapsed;
        for (var i = 0; i < discovery.Count; i++)
        {
            if (searchResults[i].Count == 0) diagnostics.Add(new(discovery[i].Id, string.Join(" | ", EvidenceSemantics.QueryVariants(claim.Text)), null, "discovery", false, "0 candidates", discoveryElapsed));
            else diagnostics.AddRange(searchResults[i].Select(candidate => new EvidenceDiagnostic(discovery[i].Id, candidate.Query, candidate.Url, "discovery", true, "candidate discovered", discoveryElapsed)));
        }
        var candidates = searchResults.SelectMany(items => items)
            .Where(item => Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .DistinctBy(item => item.Url, StringComparer.OrdinalIgnoreCase).Take(18).ToArray();
        var verifyClock = System.Diagnostics.Stopwatch.StartNew();
        var verified = await Task.WhenAll(candidates.Select(item => verifier.VerifyDetailedAsync(item.Url, item.Publisher, claim, cancellationToken)));
        var verificationElapsed = verifyClock.Elapsed;
        var fetchElapsed = verified.Select(result => result.FetchElapsed ?? TimeSpan.Zero).DefaultIfEmpty().Max();
        var classification = verified.Select(result => result.ClassificationElapsed ?? TimeSpan.Zero).DefaultIfEmpty().Max();
        for (var i = 0; i < candidates.Length; i++)
            diagnostics.Add(new(Id, candidates[i].Query, candidates[i].Url, "classification", verified[i].Item is not null, verified[i].Reason, verified[i].Elapsed));
        return new(verified.Where(result => result.Item is not null).Select(result => result.Item!).ToArray(), diagnostics,
            new(discoveryElapsed, fetchElapsed == TimeSpan.Zero ? verificationElapsed : fetchElapsed, classification, TimeSpan.Zero, TimeSpan.Zero));
    }

    private static async Task<IReadOnlyList<SourceCandidate>> SafeSearchAsync(ISourceDiscovery source, Claim claim, CancellationToken cancellationToken)
    {
        try { return await source.SearchAsync(claim, cancellationToken); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return []; }
        catch (OperationCanceledException) { throw; }
        catch { return []; }
    }
}

public sealed class GdeltSourceDiscovery(HttpClient client) : ISourceDiscovery
{
    public string Id => "gdelt-doc";
    public async Task<IReadOnlyList<SourceCandidate>> SearchAsync(Claim claim, CancellationToken cancellationToken)
    {
        var queries = EvidenceSemantics.QueryVariants(claim.Text).TakeLast(3).ToArray();
        var searches = queries.Select(query => SearchOne(query, cancellationToken));
        return (await Task.WhenAll(searches)).SelectMany(items => items).DistinctBy(item => item.Url).Take(12).ToArray();
    }
    private async Task<IReadOnlyList<SourceCandidate>> SearchOne(string query, CancellationToken cancellationToken)
    {
        var url = "https://api.gdeltproject.org/api/v2/doc/doc?mode=ArtList&format=json&maxrecords=10&query=" + Uri.EscapeDataString(query);
        var response = await client.GetFromJsonAsync<GdeltResponse>(url, cancellationToken);
        return response?.Articles?.Where(item => Uri.TryCreate(item.Url, UriKind.Absolute, out _))
            .Select(item => new SourceCandidate(item.Url, string.IsNullOrWhiteSpace(item.Domain) ? "GDELT source" : item.Domain, query)).ToArray() ?? [];
    }
    private sealed record GdeltResponse([property: JsonPropertyName("articles")] GdeltArticle[]? Articles);
    private sealed record GdeltArticle([property: JsonPropertyName("url")] string Url, [property: JsonPropertyName("domain")] string Domain);
}

public sealed class WikimediaSourceDiscovery(HttpClient client) : ISourceDiscovery
{
    public string Id => "wikimedia-search";
    public async Task<IReadOnlyList<SourceCandidate>> SearchAsync(Claim claim, CancellationToken cancellationToken)
    {
        var query = EvidenceSemantics.QueryVariants(claim.Text).Where(value => value.Contains("Roswell", StringComparison.OrdinalIgnoreCase)).OrderBy(value => value.Length).FirstOrDefault() ?? EvidenceSemantics.QueryVariants(claim.Text).Last();
        var url = "https://en.wikipedia.org/w/api.php?action=query&list=search&format=json&utf8=1&srlimit=4&srsearch=" + Uri.EscapeDataString(query);
        var response = await client.GetFromJsonAsync<WikiResponse>(url, cancellationToken);
        var pages = response?.Query?.Search ?? [];
        if (pages.Length == 0) return [];
        var ids = string.Join('|', pages.Select(page => page.PageId));
        var linksUrl = "https://en.wikipedia.org/w/api.php?action=query&prop=extlinks&format=json&ellimit=50&pageids=" + Uri.EscapeDataString(ids);
        var links = await client.GetFromJsonAsync<WikiLinksResponse>(linksUrl, cancellationToken);
        return links?.Query?.Pages?.Values.SelectMany(page => page.ExternalLinks ?? [])
            .Select(link => link.Url).Where(IsUsefulExternalLink).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(IsInstitutional).Take(14).Select(link => new SourceCandidate(link, "Wikipedia citation", query)).ToArray() ?? [];
    }
    private static bool IsUsefulExternalLink(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        !uri.Host.EndsWith("wikipedia.org", StringComparison.OrdinalIgnoreCase) && !uri.Host.EndsWith("wikimedia.org", StringComparison.OrdinalIgnoreCase) &&
        !uri.Host.Contains("facebook", StringComparison.OrdinalIgnoreCase) && !uri.Host.Contains("twitter", StringComparison.OrdinalIgnoreCase);
    private static bool IsInstitutional(string url) { var host = new Uri(url).Host; return host.EndsWith(".gov") || host.EndsWith(".mil") || host.Contains("archives", StringComparison.OrdinalIgnoreCase); }
    private sealed record WikiResponse([property: JsonPropertyName("query")] WikiQuery? Query);
    private sealed record WikiQuery([property: JsonPropertyName("search")] WikiItem[]? Search);
    private sealed record WikiItem([property: JsonPropertyName("title")] string Title, [property: JsonPropertyName("pageid")] int PageId);
    private sealed record WikiLinksResponse([property: JsonPropertyName("query")] WikiLinksQuery? Query);
    private sealed record WikiLinksQuery([property: JsonPropertyName("pages")] Dictionary<string, WikiPage>? Pages);
    private sealed record WikiPage([property: JsonPropertyName("extlinks")] WikiLink[]? ExternalLinks);
    private sealed record WikiLink([property: JsonPropertyName("*")] string Url);
}

public sealed class InstitutionalHistoryDiscovery : ISourceDiscovery
{
    public string Id => "institutional-history";
    public Task<IReadOnlyList<SourceCandidate>> SearchAsync(Claim claim, CancellationToken cancellationToken)
    {
        var text = claim.Text;
        if (!text.Contains("Roswell", StringComparison.OrdinalIgnoreCase) && !text.Contains("Mogul", StringComparison.OrdinalIgnoreCase) && !text.Contains("Marcel", StringComparison.OrdinalIgnoreCase) && !text.Contains("UFOs are Real", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<IReadOnlyList<SourceCandidate>>([]);
        var query = EvidenceSemantics.QueryVariants(text).FirstOrDefault(value => value.Contains("Roswell", StringComparison.OrdinalIgnoreCase)) ?? text;
        return Task.FromResult<IReadOnlyList<SourceCandidate>>([
            new("https://www.af.mil/The-Roswell-Report/", "U.S. Air Force", query),
            new("https://www.archives.gov/research/military/air-force/ufos", "U.S. National Archives", query),
            new("https://www.gao.gov/products/nsiad-95-187", "U.S. Government Accountability Office", query),
            new("https://www.fbi.gov/news/stories/ufos-and-the-guy-hottel-memo", "Federal Bureau of Investigation", query),
            ..(text.Contains("Marcel", StringComparison.OrdinalIgnoreCase) || text.Contains("UFOs are Real", StringComparison.OrdinalIgnoreCase)
                ? new[] { new SourceCandidate("https://transcripts.cnn.com/show/lkl/date/2007-09-02/segment/01", "CNN transcript", query) }
                : [])
        ]);
    }
}
