using SaelBrowser.Core.Facts;
using System.Diagnostics;

namespace SaelBrowser.Core.Evidence;

public sealed class EvidenceEngine(IReadOnlyList<IEvidenceProvider> providers, TimeSpan? providerTimeout = null)
{
    private readonly TimeSpan _timeout = providerTimeout ?? TimeSpan.FromSeconds(6);

    public async Task<EvidenceSet> EvaluateAsync(Claim claim, ArticleInput article, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        TimeSpan? firstEvidenceElapsed = null;
        if (!claim.IsFactual) return Empty(claim, "Twierdzenie nie jest faktem nadającym się do oceny.");
        var errors = new List<string>();
        var items = new List<EvidenceItem>();
        var diagnostics = new List<EvidenceDiagnostic>();
        var discoveryElapsed = TimeSpan.Zero; var fetchElapsed = TimeSpan.Zero; var classificationElapsed = TimeSpan.Zero;
        var providerTasks = providers.Select(RunProviderAsync).ToArray();
        var providerResults = await Task.WhenAll(providerTasks);
        foreach (var providerResult in providerResults)
        {
            if (providerResult.Error is not null) errors.Add(providerResult.Error);
            diagnostics.AddRange(providerResult.Result.Diagnostics);
            if (providerResult.Result.Timings.Discovery > discoveryElapsed) discoveryElapsed = providerResult.Result.Timings.Discovery;
            if (providerResult.Result.Timings.Fetch > fetchElapsed) fetchElapsed = providerResult.Result.Timings.Fetch;
            if (providerResult.Result.Timings.Classification > classificationElapsed) classificationElapsed = providerResult.Result.Timings.Classification;
            if (providerResult.Result.Items.Count > 0 && (firstEvidenceElapsed is null || providerResult.Completed < firstEvidenceElapsed)) firstEvidenceElapsed = providerResult.Completed;
            items.AddRange(providerResult.Result.Items);
        }
        var valid = items.Where(i => i.ClaimId == claim.Id && i.Origin != EvidenceOrigin.PageContent &&
            Uri.TryCreate(i.Url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && i.Confidence is >= 0 and <= 1).ToArray();
        var clusteringClock = Stopwatch.StartNew();
        var clusters = Cluster(valid);
        var clusteringElapsed = clusteringClock.Elapsed;
        var supports = clusters.Where(c => c.Any(i => i.Stance == EvidenceStance.Supports)).ToArray();
        var refutes = clusters.Where(c => c.Any(i => i.Stance == EvidenceStance.Refutes)).ToArray();
        var support = Combined(supports);
        var refute = Combined(refutes);
        var conflict = support >= .5 && refute >= .5;
        var winningCount = support >= refute ? supports.Length : refutes.Length;
        var winningStance = support >= refute ? EvidenceStance.Supports : EvidenceStance.Refutes;
        var hasTrustedDirectEvidence = valid.Any(item => item.Stance == winningStance && item.Direct &&
            item.SourceType is SourceType.FactCheck or SourceType.PrimaryOfficial or SourceType.PrimaryDocument or SourceType.Academic);
        var sufficient = !conflict && winningCount >= 2 && hasTrustedDirectEvidence && Math.Max(support, refute) >= .8;
        var message = conflict ? "Źródła są sprzeczne." : clusters.Length == 0 ? "Brak wystarczających dowodów." :
            winningCount < 2 ? "Źródła nie są wystarczająco niezależne." :
            !hasTrustedDirectEvidence ? "Brak bezpośredniego dowodu z zaufanego typu źródła." :
            !sufficient ? "Dowody nie przekroczyły bezpiecznego progu." : "Niezależne dowody przekroczyły bezpieczny próg.";
        var traceClusters = clusters.Select((cluster, index) => new EvidenceCluster($"cluster-{index + 1}", cluster.ToArray(), cluster.Max(Quality))).ToArray();
        foreach (var cluster in traceClusters.Where(cluster => cluster.Items.Count > 1))
            diagnostics.AddRange(cluster.Items.Skip(1).Select(item => new EvidenceDiagnostic("evidence-engine", claim.Text, item.Url, "clustering", false, "same origin cluster", clusteringElapsed)));
        return new(claim, valid, support, refute, conflict, sufficient, message, errors, traceClusters, firstEvidenceElapsed, clock.Elapsed,
            diagnostics, new(discoveryElapsed, fetchElapsed, classificationElapsed, clusteringElapsed, TimeSpan.Zero));

        async Task<ProviderRun> RunProviderAsync(IEvidenceProvider provider)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            try
            {
                var detailed = provider is IDiagnosticEvidenceProvider diagnostic
                    ? await diagnostic.FindDetailedAsync(claim, article, timeout.Token)
                    : new EvidenceProviderResult(await provider.FindAsync(claim, article, timeout.Token), [], new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));
                return new(detailed, clock.Elapsed, null);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new(new([], [new(provider.Id, claim.Text, null, "fetch", false, "timeout", _timeout)], new(TimeSpan.Zero, _timeout, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero)), clock.Elapsed, $"{provider.Id}: timeout");
            }
            catch (Exception ex)
            {
                return new(new([], [new(provider.Id, claim.Text, null, "fetch", false, "provider unavailable: " + ex.GetType().Name, TimeSpan.Zero)], new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero)), clock.Elapsed, $"{provider.Id}: unavailable");
            }
        }
    }

    private sealed record ProviderRun(EvidenceProviderResult Result, TimeSpan Completed, string? Error);

    private static double Combined(IEnumerable<IGrouping<string, EvidenceItem>> clusters)
    {
        var scores = clusters.Select(c => c.Max(Quality)).OrderDescending().ToArray();
        if (scores.Length == 0) return 0;
        if (scores[0] < .5) return Math.Min(.49, scores[0]);
        return Math.Min(.98, 1 - scores.Aggregate(1d, (remaining, score) => remaining * (1 - score)));
    }
    private static IGrouping<string, EvidenceItem>[] Cluster(EvidenceItem[] items)
    {
        var parent = Enumerable.Range(0, items.Length).ToArray();
        int Root(int index)
        {
            while (parent[index] != index) { parent[index] = parent[parent[index]]; index = parent[index]; }
            return index;
        }
        void Union(int first, int second)
        {
            var left = Root(first); var right = Root(second);
            if (left != right) parent[right] = left;
        }
        for (var first = 0; first < items.Length; first++)
            for (var second = first + 1; second < items.Length; second++)
                if (CanonicalDomain(items[first].Url) == CanonicalDomain(items[second].Url) ||
                    (!string.IsNullOrWhiteSpace(items[first].PrimarySourceId) && items[first].PrimarySourceId == items[second].PrimarySourceId) ||
                    Similarity(items[first].Summary, items[second].Summary) >= .72)
                    Union(first, second);
        return Enumerable.Range(0, items.Length).GroupBy(Root, index => items[index]).Select((group, index) =>
            new EvidenceGrouping(index.ToString(), group)).Cast<IGrouping<string, EvidenceItem>>().ToArray();
    }
    private sealed class EvidenceGrouping(string key, IEnumerable<EvidenceItem> items) : List<EvidenceItem>(items), IGrouping<string, EvidenceItem>
    {
        public string Key { get; } = key;
    }
    private static double Similarity(string first, string second)
    {
        static HashSet<string> Tokens(string value) => value.ToLowerInvariant()
            .Split([' ', ',', '.', ':', ';', '-', '–', '—', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 4).ToHashSet();
        var left = Tokens(first); var right = Tokens(second);
        if (left.Count == 0 || right.Count == 0) return 0;
        return left.Intersect(right).Count() / (double)left.Union(right).Count();
    }
    private static double Quality(EvidenceItem item)
    {
        var weight = item.SourceType switch
        {
            SourceType.PrimaryOfficial => .9, SourceType.PrimaryDocument => .92,
            SourceType.FactCheck => .86, SourceType.Academic => .9,
            SourceType.NewsReport => .68, SourceType.Secondary => .5,
            SourceType.UserGenerated => .25, _ => .3
        };
        return weight * (item.Direct ? 1 : .82) * item.Confidence;
    }
    private static string CanonicalDomain(string url)
    {
        var host = new Uri(url).Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }
    private static EvidenceSet Empty(Claim claim, string message) => new(claim, [], 0, 0, false, false, message, []);
}
