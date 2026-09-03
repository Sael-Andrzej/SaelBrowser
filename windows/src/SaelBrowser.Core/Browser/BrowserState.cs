using System.Globalization;
using System.Text;

namespace SaelBrowser.Core.Browser;

public static class BrowserAddressNormalizer
{
    public static string? Normalize(string? input)
    {
        var value = input?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) &&
            absolute.Scheme is "https" or "http")
            return absolute.AbsoluteUri;

        if (!value.Contains(' ') && value.Contains('.') &&
            Uri.TryCreate($"https://{value}", UriKind.Absolute, out var hostUri))
            return hostUri.AbsoluteUri;

        return "https://www.google.com/search?q=" + Uri.EscapeDataString(value);
    }
}

public enum BrowserMode { Original, Sael }

public sealed class BrowserModeState(BrowserMode initial = BrowserMode.Sael)
{
    public BrowserMode Mode { get; private set; } = initial;
    public bool Select(BrowserMode mode)
    {
        if (Mode == mode) return false;
        Mode = mode;
        return true;
    }
}

public readonly record struct AnalysisToken(long Generation, string Url);

public sealed class AnalysisRequestGate
{
    private long _generation;
    public void BeginNavigation() => Interlocked.Increment(ref _generation);
    public AnalysisToken Capture(string url) => new(Interlocked.Read(ref _generation), url);
    public bool IsCurrent(AnalysisToken token, string? currentUrl) =>
        token.Generation == Interlocked.Read(ref _generation) &&
        string.Equals(token.Url, currentUrl ?? string.Empty, StringComparison.Ordinal);
}
