using AngleSharp;
using AngleSharp.Dom;
using SaelBrowser.Core.Facts;

namespace SaelBrowser.Core.Articles;

public interface IArticleExtractor
{
    Task<ArticleInput> ExtractAsync(string html, string url, CancellationToken cancellationToken = default);
}

public sealed class ArticleExtractor : IArticleExtractor
{
    private static readonly string[] Excluded =
    [
        "script", "style", "noscript", "nav", "footer", "aside", "form", "figure", "figcaption", "[role=figure]",
        "[role=navigation]", "[role=complementary]", "[class*=comment i]",
        "[id*=comment i]", "[class*=advert i]", "[class*=cookie i]", ".adsbygoogle", ".sael-progress", "#sael-page-status"
    ];

    public async Task<ArticleInput> ExtractAsync(string html, string url, CancellationToken cancellationToken = default)
    {
        var domain = Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? RemoveWww(uri.Host) : string.Empty;
        if (string.IsNullOrWhiteSpace(html)) return new("", "", url, domain);

        try
        {
            var context = BrowsingContext.New(Configuration.Default);
            var document = await context.OpenAsync(request => request.Content(html).Address(url), cancellationToken);
            // The live value of Google's search box is a DOM property and is not
            // guaranteed to be reflected back into outerHTML as a value attribute.
            // The navigation URL is the stable source of truth for a search claim.
            var searchQuery = IsSearchPage(uri)
                ? FirstOrNull(QueryParameter(uri!, "q"), document.QuerySelector("input[name=q]")?.GetAttribute("value"))
                : null;
            foreach (var selector in Excluded)
                foreach (var node in document.QuerySelectorAll(selector).ToArray()) node.Remove();

            var main = document.QuerySelector("article, main, [role=main]") ?? document.Body;
            var title = First(
                searchQuery,
                document.QuerySelector("meta[property='og:title']")?.GetAttribute("content"),
                document.QuerySelector("meta[name='twitter:title']")?.GetAttribute("content"),
                main?.QuerySelector("h1")?.TextContent,
                document.Title);
            var author = FirstOrNull(
                document.QuerySelector("meta[name=author]")?.GetAttribute("content"),
                document.QuerySelector("meta[property='article:author']")?.GetAttribute("content"),
                main?.QuerySelector("[rel=author], [itemprop=author]")?.TextContent);
            var published = FirstOrNull(
                document.QuerySelector("meta[property='article:published_time']")?.GetAttribute("content"),
                document.QuerySelector("meta[itemprop=datePublished]")?.GetAttribute("content"),
                main?.QuerySelector("time[datetime]")?.GetAttribute("datetime"));
            var sources = main?.QuerySelectorAll("a[href]")
                .Select(a => a.GetAttribute("href"))
                .Where(href => Uri.TryCreate(uri, href, out var link) && link.Scheme == "https" && link.Host != uri?.Host)
                .Select(href => new Uri(uri!, href!).AbsoluteUri)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(20)
                .Select(link => new FactSource(new Uri(link).Host, link)).ToArray() ?? [];
            return new ArticleInput(title, ReadableText(main), url, domain, published, author, sources);
        }
        catch (OperationCanceledException) { throw; }
        catch { return new("", "", url, domain); }
    }

    private static string First(params string?[] values) => FirstOrNull(values) ?? "";
    private static string? FirstOrNull(params string?[] values) => values.Select(Normalize).FirstOrDefault(v => v.Length > 0);
    private static string Normalize(string? value) => string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string ReadableText(IElement? root)
    {
        if (root is null) return "";
        var blocks = root.QuerySelectorAll("p,h2,h3,li")
            .Where(element => !element.Ancestors().OfType<IElement>().Any(ancestor => ancestor != root && ancestor.Matches("p,li")))
            .Select(element => Normalize(element.TextContent))
            .Where(text => text.Length >= 8)
            .Select(text => EndsWithSentencePunctuation(text) ? text : text + ".")
            .ToArray();
        return blocks.Length > 0 ? string.Join(Environment.NewLine, blocks) : Normalize(root.TextContent);
    }
    private static bool EndsWithSentencePunctuation(string text)
    {
        var index = text.Length - 1;
        while (index >= 0 && text[index] == ']')
        {
            var open = text.LastIndexOf('[', index);
            if (open < 0 || !text.AsSpan(open + 1, index - open - 1).ToString().All(char.IsDigit)) break;
            index = open - 1;
        }
        return index >= 0 && ".!?".Contains(text[index]);
    }
    private static string RemoveWww(string value) => value.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? value[4..] : value;
    private static bool IsSearchPage(Uri? uri) => uri is not null && (uri.Host.Equals("google.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase));
    private static string? QueryParameter(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (!Uri.UnescapeDataString(parts[0]).Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            try { return Uri.UnescapeDataString((parts.Length == 2 ? parts[1] : "").Replace('+', ' ')); }
            catch (UriFormatException) { return null; }
        }
        return null;
    }
}
