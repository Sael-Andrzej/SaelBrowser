namespace SaelBrowser.Core.Facts;

public enum FactVerdict { True, False, Unknown }
public enum EvidenceStance { Supports, Refutes, Neutral, Unknown }
public enum EvidenceOrigin { PageContent, ExternalApi, VerifiedDatabase }
public enum SourceType { PrimaryOfficial, PrimaryDocument, FactCheck, NewsReport, Academic, Secondary, UserGenerated, Unknown }

public sealed record ArticleInput(
    string Title, string Content, string Url, string Domain,
    string? PublishedAt = null, string? Author = null,
    IReadOnlyList<FactSource>? CitedSources = null);

public sealed record FactSource(string Name, string Url, string? PublishedAt = null);
public sealed record ClickbaitResult(double Score, IReadOnlyList<string> Reasons);
public sealed record Claim(string Id, string Text, bool IsFactual, double Priority, string? ClaimDate = null);

public sealed record EvidenceItem(
    string ClaimId, string Summary, string Url, string Domain, string Publisher,
    string? PublicationDate, SourceType SourceType, EvidenceStance Stance,
    double Confidence, EvidenceOrigin Origin, string? PrimarySourceId = null,
    bool Direct = false);

public sealed record EvidenceSet(
    Claim Claim, IReadOnlyList<EvidenceItem> Items, double SupportConfidence,
    double RefuteConfidence, bool Conflict, bool Sufficient, string Message,
    IReadOnlyList<string> ProviderErrors,
    IReadOnlyList<EvidenceCluster>? Clusters = null,
    TimeSpan? FirstEvidenceElapsed = null,
    TimeSpan? TotalElapsed = null,
    IReadOnlyList<EvidenceDiagnostic>? Diagnostics = null,
    EvidenceStageTimings? StageTimings = null);

public sealed record EvidenceDiagnostic(
    string Provider, string Query, string? CandidateUrl, string Stage,
    bool Accepted, string Reason, TimeSpan Elapsed);

public sealed record EvidenceStageTimings(
    TimeSpan Discovery, TimeSpan Fetch, TimeSpan Classification,
    TimeSpan Clustering, TimeSpan FactEngine);

public sealed record EvidenceCluster(string Id, IReadOnlyList<EvidenceItem> Items, double Confidence);

public sealed record FactResult(
    FactVerdict Verdict, double Confidence, string Rationale,
    ClickbaitResult Clickbait, IReadOnlyList<Claim> Claims,
    IReadOnlyList<EvidenceSet> EvidenceSets,
    IReadOnlyList<FactSource> Sources);
