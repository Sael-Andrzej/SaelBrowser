using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SaelBrowser.Core.Facts;

namespace SaelBrowser.Core.Evidence;

public interface IEvidenceProvider
{
    string Id { get; }
    Task<IReadOnlyList<EvidenceItem>> FindAsync(Claim claim, ArticleInput article, CancellationToken cancellationToken);
}

public sealed record EvidenceProviderResult(
    IReadOnlyList<EvidenceItem> Items,
    IReadOnlyList<EvidenceDiagnostic> Diagnostics,
    EvidenceStageTimings Timings);

public interface IDiagnosticEvidenceProvider : IEvidenceProvider
{
    Task<EvidenceProviderResult> FindDetailedAsync(Claim claim, ArticleInput article, CancellationToken cancellationToken);
}

public sealed class RemoteEvidenceProvider : IDiagnosticEvidenceProvider
{
    private readonly HttpClient _client;
    private readonly Uri[] _endpoints;
    private readonly IPublicSourceVerifier? _verifier;
    public string Id => "sael-evidence-backend";

    public RemoteEvidenceProvider(HttpClient client, params string[] baseUrls)
        : this(client, null, baseUrls) { }

    public RemoteEvidenceProvider(HttpClient client, IPublicSourceVerifier? verifier, params string[] baseUrls)
    {
        _client = client;
        _verifier = verifier;
        _endpoints = baseUrls.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct()
            .Select(v => new Uri(new Uri(v.TrimEnd('/') + "/"), "api/v1/evidence")).ToArray();
        if (_endpoints.Any(uri => uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Evidence backend requires HTTPS.");
    }

    public async Task<IReadOnlyList<EvidenceItem>> FindAsync(Claim claim, ArticleInput article, CancellationToken cancellationToken)
        => (await FindDetailedAsync(claim, article, cancellationToken)).Items;

    public async Task<EvidenceProviderResult> FindDetailedAsync(Claim claim, ArticleInput article, CancellationToken cancellationToken)
    {
        var diagnostics = new List<EvidenceDiagnostic>();
        var fetch = TimeSpan.Zero; var classification = TimeSpan.Zero;
        foreach (var query in EvidenceSemantics.QueryVariants(claim.Text))
        {
            var primary = await RequestAsync(_endpoints[0], query);
            if (primary.Items.Count > 0) return Result(primary.Items);
            if (!primary.TransientFailure) continue;

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            var retry = await RequestAsync(_endpoints[0], query);
            if (retry.Items.Count > 0) return Result(retry.Items);
            if (!retry.TransientFailure || _endpoints.Length < 2) continue;

            var fallback = await RequestAsync(_endpoints[1], query);
            if (fallback.Items.Count > 0) return Result(fallback.Items);
        }
        return Result([]);

        EvidenceProviderResult Result(IReadOnlyList<EvidenceItem> found) =>
            new(found, diagnostics, new(TimeSpan.Zero, fetch, classification, TimeSpan.Zero, TimeSpan.Zero));

        async Task<EndpointAttempt> RequestAsync(Uri endpoint, string query)
        {
            var requestClock = System.Diagnostics.Stopwatch.StartNew();
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(TimeSpan.FromMilliseconds(2500));
            try
            {
                using var response = await _client.PostAsJsonAsync(endpoint, new EvidenceRequest(
                    query[..Math.Min(500, query.Length)], query == claim.Text ? "pl" : "en", PublicOrigin(article.Url), DateOnlyValue(article.PublishedAt)), requestTimeout.Token);
                fetch += requestClock.Elapsed;
                if ((int)response.StatusCode is 502 or 503 or 504)
                {
                    diagnostics.Add(Diagnostic(query, endpoint, "fetch", false, $"provider unavailable ({(int)response.StatusCode})", requestClock.Elapsed));
                    return new([], true);
                }
                if (!response.IsSuccessStatusCode)
                {
                    diagnostics.Add(Diagnostic(query, endpoint, "fetch", false, $"HTTP {(int)response.StatusCode}", requestClock.Elapsed));
                    return new([], false);
                }
                await using var stream = await response.Content.ReadAsStreamAsync(requestTimeout.Token);
                var body = await JsonSerializer.DeserializeAsync<EvidenceResponse>(stream, JsonOptions, requestTimeout.Token);
                if (body is null || Normalize(body.Query) != Normalize(query))
                {
                    diagnostics.Add(Diagnostic(query, endpoint, "classification", false, "invalid or mismatched provider response", requestClock.Elapsed));
                    return new([], false);
                }
                diagnostics.AddRange(body.Warnings.Select(warning => Diagnostic(query, endpoint, "provider-warning", false, warning, requestClock.Elapsed)));
                var mapped = new List<EvidenceItem>();
                foreach (var item in body.Evidence.Take(10))
                {
                    var classifyClock = System.Diagnostics.Stopwatch.StartNew();
                    var evidence = Map(item, claim.Id, claim.Text);
                    if (evidence is not null && evidence.Stance != EvidenceStance.Unknown) { mapped.Add(evidence); diagnostics.Add(new(Id, query, item.Url, "classification", true, "explicit claim classified", classifyClock.Elapsed)); }
                    else if (item.Provenance == "BRAVE_SEARCH" && _verifier is not null)
                    {
                        var verified = await _verifier.VerifyDetailedAsync(item.Url, item.Publisher, claim, cancellationToken);
                        diagnostics.Add(new(Id, query, item.Url, "classification", verified.Item is not null, verified.Reason, verified.Elapsed));
                        if (verified.Item is not null) mapped.Add(verified.Item);
                    }
                    else diagnostics.Add(new(Id, query, item.Url, "classification", false, evidence is null ? "invalid provider candidate" : "no explicit claim", classifyClock.Elapsed));
                    classification += classifyClock.Elapsed;
                }
                if (body.Evidence.Length == 0) diagnostics.Add(Diagnostic(query, endpoint, "discovery", false, "provider returned 0 candidates", requestClock.Elapsed));
                return new(mapped, false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                diagnostics.Add(Diagnostic(query, endpoint, "fetch", false, "timeout", requestClock.Elapsed));
                return new([], true);
            }
            catch (HttpRequestException ex)
            {
                diagnostics.Add(Diagnostic(query, endpoint, "fetch", false, "access failure: " + ex.Message, requestClock.Elapsed));
                return new([], true);
            }
            catch (JsonException)
            {
                diagnostics.Add(Diagnostic(query, endpoint, "classification", false, "invalid provider response", requestClock.Elapsed));
                return new([], false);
            }
        }
    }

    private sealed record EndpointAttempt(IReadOnlyList<EvidenceItem> Items, bool TransientFailure);

    private EvidenceDiagnostic Diagnostic(string query, Uri endpoint, string stage, bool accepted, string reason, TimeSpan elapsed) =>
        new(Id, query, endpoint.AbsoluteUri, stage, accepted, reason, elapsed);

    private static EvidenceItem? Map(EvidenceDto item, string claimId, string requestedClaim)
    {
        if (!Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)) return null;
        var domain = RemoveWww(uri.Host.ToLowerInvariant());
        if (domain != RemoveWww(item.Domain.ToLowerInvariant()) || IsLocal(domain)) return null;
        if (item.ProviderConfidence is < 0 or > 1) return null;
        var contractValid = item.Provenance switch
        {
            "GOOGLE_FACT_CHECK" => item.Provider == "google-fact-check" && item.SourceType == "FACT_CHECK" && item.Stance == "UNKNOWN" && item.ProviderConfidence <= .8,
            "BRAVE_SEARCH" => item.Provider == "brave-search" && item.SourceType == "UNKNOWN" && item.Stance == "UNKNOWN" && item.ProviderConfidence <= .5,
            _ => false
        };
        if (!contractValid || string.IsNullOrWhiteSpace(item.Snippet) || string.IsNullOrWhiteSpace(item.Publisher)) return null;
        var stance = EvidenceSemantics.FactCheckStance(requestedClaim, item.Claim, item.Snippet);
        return new EvidenceItem(claimId, item.Snippet, item.Url, domain, item.Publisher,
            item.PublishedAt, ParseSourceType(item.SourceType), stance,
            item.ProviderConfidence, EvidenceOrigin.ExternalApi,
            item.Provenance == "GOOGLE_FACT_CHECK" ? item.Url : null, item.Provenance == "GOOGLE_FACT_CHECK" && stance != EvidenceStance.Unknown);
    }

    private static bool IsLocal(string host) => host is "localhost" or "127.0.0.1" or "0.0.0.0" || host.EndsWith(".localhost", StringComparison.Ordinal);
    private static string Normalize(string value) => string.Join(' ', value.Normalize().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string? PublicOrigin(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo)
        ? new UriBuilder("https", uri.Host, uri.IsDefaultPort ? -1 : uri.Port).Uri.AbsoluteUri : null;
    private static DateOnly? DateOnlyValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateOnly.TryParse(value[..Math.Min(10, value.Length)], out var date) ? date : null;
    }
    private static string RemoveWww(string value) => value.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? value[4..] : value;
    private static SourceType ParseSourceType(string value) => value switch { "FACT_CHECK" => SourceType.FactCheck, _ => SourceType.Unknown };

    private sealed record EvidenceRequest(string Claim, string Language, string? SourceUrl, DateOnly? PublishedAt);
    private sealed record EvidenceResponse(string Query, EvidenceDto[] Evidence, string[] Warnings, bool CacheHit);
    private sealed record EvidenceDto(string Claim, string Snippet, string Url, string Domain, string Publisher, string? PublishedAt,
        string SourceType, string Stance, string Provenance, string Provider, double ProviderConfidence);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };
}
