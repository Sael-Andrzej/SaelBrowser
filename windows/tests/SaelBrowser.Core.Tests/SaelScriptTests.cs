namespace SaelBrowser.Core.Tests;

public sealed class SaelScriptTests
{
    private static string Script => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sael.js"));
    [Fact] public void ScriptSupportsApplyRestoreAndDynamicContent()
    {
        Assert.Contains("action === 'restore'", Script);
        Assert.Contains("MutationObserver", Script);
        Assert.Contains("observer.observe", Script);
    }
    [Fact] public void ScriptProtectsLoginAndPaymentDialogs()
    {
        Assert.Contains("login", Script);
        Assert.Contains("payment", Script);
        Assert.Contains("protectedWords.some", Script);
    }
    [Fact] public void ScriptDoesNotBlanketRemoveIframesModalsOrBanners()
    {
        Assert.DoesNotContain("querySelectorAll('iframe')", Script);
        Assert.DoesNotContain("querySelectorAll('modal')", Script);
        Assert.DoesNotContain("querySelectorAll('banner')", Script);
    }
    [Fact] public void ScriptHandlesDynamicAndPremiumOverlays()
    {
        Assert.Contains("body > div,body > aside", Script);
        Assert.Contains("premium", Script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nuisanceOverlay(el)", Script);
        Assert.Contains("MutationObserver", Script);
    }
    [Fact] public void ScriptRewritesClickbaitAndRestoresOriginalTitle()
    {
        Assert.Contains("rewriteTitle", Script);
        Assert.Contains("linkedTitle", Script);
        Assert.Contains("data-sael-original-title", Script);
        Assert.Contains("info.originalHtml", Script);
        Assert.DoesNotContain("NIEZWERYFIKOWANE", Script);
    }
    [Fact] public void ProgressiveUiBlursOnlyAnalyzedTextAndRestoreRemovesState()
    {
        Assert.Contains("sael-analysis-text", Script);
        Assert.Contains("filter:blur(2.2px)", Script);
        Assert.Contains("SAEL analizuje…", Script);
        Assert.Contains("sael-analyzing", Script);
        Assert.Contains("el.innerHTML = info.originalHtml", Script);
        Assert.Contains("removeAttribute('data-sael-analyzing')", Script);
        Assert.DoesNotContain("body{filter", Script, StringComparison.OrdinalIgnoreCase);
    }
}
