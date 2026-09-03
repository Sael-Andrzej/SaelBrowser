using System.Text.Json;
using SaelBrowser.Core.Articles;
using SaelBrowser.Core.Evidence;
using SaelBrowser.Core.Facts;

var claims = args.Length > 0 ? args : ["The Earth is round", "The Earth is flat", "The Eiffel Tower is in Paris"];
var extractor = new ArticleExtractor();
var backendHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
backendHttp.DefaultRequestHeaders.UserAgent.ParseAdd("SaelBrowser-EvidenceProbe/0.1");
var fetchHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(6) };
var verifier = new PublicSourceVerifier(fetchHttp, extractor);
var discoveryHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
discoveryHttp.DefaultRequestHeaders.UserAgent.ParseAdd("SaelBrowser-EvidenceProbe/0.1");
var providers = new IEvidenceProvider[]
{
    new RemoteEvidenceProvider(backendHttp, verifier, "https://api.xn--ypay-99a.pl", "https://api.alvsal.pl"),
    new DiscoveryEvidenceProvider([new InstitutionalHistoryDiscovery()], verifier),
    new DiscoveryEvidenceProvider([new GdeltSourceDiscovery(discoveryHttp), new WikimediaSourceDiscovery(discoveryHttp)], verifier)
};
var engine = new FactEngine(new ClaimExtractor(), new ClickbaitAnalyzer(), new EvidenceEngine(providers));
var traces = new List<object>();
foreach (var claim in claims)
{
    var result = await engine.EvaluateAsync(new ArticleInput(claim, claim + ".", "https://probe.sael.invalid/", "probe.sael.invalid"));
    traces.Add(new
    {
        claim,
        verdict = result.Verdict.ToString().ToUpperInvariant(),
        confidence = result.Confidence,
        rationale = result.Rationale,
        sets = result.EvidenceSets.Select(set => new
        {
            extractedClaim = set.Claim.Text,
            evidence = set.Items.Select(item => new { item.Publisher, item.Domain, stance = item.Stance.ToString().ToUpperInvariant(), item.Confidence, item.SourceType, item.PrimarySourceId, item.Summary, item.Url }),
            clusters = (set.Clusters ?? []).Select(cluster => new { cluster.Id, cluster.Confidence, sources = cluster.Items.Select(item => new { item.Domain, item.PrimarySourceId, stance = item.Stance.ToString().ToUpperInvariant() }) }),
            supportConfidence = set.SupportConfidence,
            refuteConfidence = set.RefuteConfidence,
            set.Sufficient,
            set.Conflict,
            set.Message,
            set.ProviderErrors,
            diagnostics = (set.Diagnostics ?? []).Select(item => new { item.Provider, item.Query, item.CandidateUrl, item.Stage, item.Accepted, item.Reason, elapsedMs = item.Elapsed.TotalMilliseconds }),
            timings = set.StageTimings
        })
    });
}
Console.WriteLine(JsonSerializer.Serialize(traces, new JsonSerializerOptions { WriteIndented = true }));
