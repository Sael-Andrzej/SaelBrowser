namespace SaelBrowser.Core.Facts;

public static class FactPresentation
{
    public static string VerdictLabel(FactVerdict verdict) => verdict switch
    {
        FactVerdict.True => "PRAWDA",
        FactVerdict.False => "FAŁSZ",
        _ => "NIE WIEM"
    };

    public static string VerdictButtonText(FactResult result) =>
        $"{VerdictLabel(result.Verdict)} • {result.Confidence:P0} • szczegóły";
}
