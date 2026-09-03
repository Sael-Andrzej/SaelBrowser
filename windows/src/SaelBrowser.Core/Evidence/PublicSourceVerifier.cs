using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using SaelBrowser.Core.Articles;
using SaelBrowser.Core.Facts;

namespace SaelBrowser.Core.Evidence;

public interface IPublicSourceVerifier
{
    Task<EvidenceItem?> VerifyAsync(string url, string publisher, Claim claim, CancellationToken cancellationToken);
    async Task<SourceVerificationResult> VerifyDetailedAsync(string url, string publisher, Claim claim, CancellationToken cancellationToken)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var item = await VerifyAsync(url, publisher, claim, cancellationToken);
        return new(item, item is null ? "no explicit claim" : "explicit claim classified", clock.Elapsed);
    }
}

public sealed record SourceVerificationResult(EvidenceItem? Item, string Reason, TimeSpan Elapsed,
    TimeSpan? FetchElapsed = null, TimeSpan? ClassificationElapsed = null);

public sealed partial class PublicSourceVerifier(HttpClient client, IArticleExtractor extractor) : IPublicSourceVerifier
{
    private const int MaxBytes = 750_000;

    public async Task<EvidenceItem?> VerifyAsync(string url, string publisher, Claim claim, CancellationToken cancellationToken)
        => (await VerifyDetailedAsync(url, publisher, claim, cancellationToken)).Item;

    public async Task<SourceVerificationResult> VerifyDetailedAsync(string url, string publisher, Claim claim, CancellationToken cancellationToken)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var fetchElapsed = TimeSpan.Zero; var classificationElapsed = TimeSpan.Zero;
        SourceVerificationResult Reject(string reason) => new(null, reason, clock.Elapsed, fetchElapsed == TimeSpan.Zero ? clock.Elapsed : fetchElapsed, classificationElapsed);
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !await IsPublicAsync(uri, cancellationToken)) return Reject("access denied or non-public address");
            if (uri.Host.EndsWith("wikipedia.org", StringComparison.OrdinalIgnoreCase)) return Reject("encyclopedic discovery source");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("SaelBrowser-Windows/0.1 evidence-reader");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes ||
                response.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) != true) return Reject($"access failure: HTTP {(int)response.StatusCode} or unsupported content");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var memory = new MemoryStream();
            var buffer = new byte[16_384];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                if (memory.Length + read > MaxBytes) return Reject("source exceeds safe size limit");
                await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            var html = System.Text.Encoding.UTF8.GetString(memory.ToArray());
            fetchElapsed = clock.Elapsed;
            var classificationClock = System.Diagnostics.Stopwatch.StartNew();
            var extracted = await extractor.ExtractAsync(html, uri.AbsoluteUri, cancellationToken);
            var text = $"{extracted.Title}. {extracted.Content}";
            var stance = EvidenceSemantics.ExplicitTextStance(claim.Text, text);
            classificationElapsed = classificationClock.Elapsed;
            if (stance == EvidenceStance.Unknown) return Reject("no explicit claim or semantic similarity too low");
            var snippet = text.Length <= 500 ? text : text[..500];
            var domain = CanonicalDomain(uri.AbsoluteUri);
            return new(new EvidenceItem(claim.Id, snippet, uri.AbsoluteUri, domain,
                string.IsNullOrWhiteSpace(publisher) ? domain : publisher, extracted.PublishedAt,
                SourceTypeFor(domain), stance, .9, EvidenceOrigin.ExternalApi,
                PrimarySource(text[..Math.Min(800, text.Length)], domain), true), "explicit claim classified", clock.Elapsed, fetchElapsed, classificationElapsed);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Reject("access failure: " + ex.GetType().Name); }
    }

    public static string PrimarySource(string text, string fallbackDomain)
    {
        var wire = WireAttribution().Match(text);
        if (wire.Success) return "wire:" + wire.Groups[1].Value.ToLowerInvariant().Replace(" ", "-");
        if ((fallbackDomain.EndsWith("af.mil") || fallbackDomain == "archives.gov") && text.Contains("Air Force", StringComparison.OrdinalIgnoreCase)) return "agency:us-air-force";
        if (fallbackDomain.EndsWith("gao.gov")) return "agency:gao";
        if (fallbackDomain.EndsWith("fbi.gov")) return "agency:fbi";
        return "domain:" + fallbackDomain;
    }
    private static async Task<bool> IsPublicAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(uri.UserInfo) || uri.IsLoopback || IPAddress.TryParse(uri.Host, out _)) return false;
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
            return addresses.Length > 0 && addresses.All(IsPublicAddress);
        }
        catch { return false; }
    }
    private static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return false;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] != 10 && bytes[0] != 127 && !(bytes[0] == 169 && bytes[1] == 254) &&
                   !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31) && !(bytes[0] == 192 && bytes[1] == 168);
        }
        var ipv6 = address.GetAddressBytes();
        return !address.Equals(IPAddress.IPv6None) && !address.Equals(IPAddress.IPv6Any) && (ipv6[0] & 0xfe) != 0xfc;
    }
    private static string CanonicalDomain(string url)
    {
        var host = new Uri(url).Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }
    private static SourceType SourceTypeFor(string domain) => domain.EndsWith(".gov", StringComparison.OrdinalIgnoreCase) ||
        domain.EndsWith(".mil", StringComparison.OrdinalIgnoreCase) ? SourceType.PrimaryOfficial : SourceType.Secondary;

    [GeneratedRegex(@"\b(Reuters|Associated Press|Agence France-Presse|AFP|Polska Agencja Prasowa|PAP)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WireAttribution();
}
