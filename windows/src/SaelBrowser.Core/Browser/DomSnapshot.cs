namespace SaelBrowser.Core.Browser;

public static class DomSnapshot
{
    public const string Script = "(() => (document.documentElement?.outerHTML || '').slice(0,1500000))()";
}
