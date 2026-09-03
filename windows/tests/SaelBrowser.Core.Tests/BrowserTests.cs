using SaelBrowser.Core.Browser;

namespace SaelBrowser.Core.Tests;

public sealed class BrowserTests
{
    [Theory]
    [InlineData("example.com", "https://example.com/")]
    [InlineData(" https://example.com/a ", "https://example.com/a")]
    public void NormalizesAddresses(string input, string expected) => Assert.Equal(expected, BrowserAddressNormalizer.Normalize(input));

    [Fact] public void BlankAddressIsRejected() => Assert.Null(BrowserAddressNormalizer.Normalize("  "));
    [Fact] public void WordsBecomeSearch() => Assert.Equal("https://www.google.com/search?q=sael%20browser", BrowserAddressNormalizer.Normalize("sael browser"));

    [Fact]
    public void ModeChangesOnlyWhenNeeded()
    {
        var state = new BrowserModeState();
        Assert.Equal(BrowserMode.Sael, state.Mode);
        Assert.False(state.Select(BrowserMode.Sael));
        Assert.True(state.Select(BrowserMode.Original));
    }

    [Fact]
    public void NavigationInvalidatesOldAnalysis()
    {
        var gate = new AnalysisRequestGate();
        var old = gate.Capture("https://one.example/");
        gate.BeginNavigation();
        Assert.False(gate.IsCurrent(old, "https://one.example/"));
        var current = gate.Capture("https://two.example/");
        Assert.True(gate.IsCurrent(current, "https://two.example/"));
        Assert.False(gate.IsCurrent(current, "https://three.example/"));
    }

    [Fact]
    public void SnapshotIncludesHeadMetadataAndHasHardSizeLimit()
    {
        Assert.Contains("document.documentElement", DomSnapshot.Script);
        Assert.Contains("slice(0,1500000)", DomSnapshot.Script);
        Assert.DoesNotContain("document.querySelector('article, main')", DomSnapshot.Script);
    }
}
