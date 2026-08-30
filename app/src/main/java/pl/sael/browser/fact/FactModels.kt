package pl.sael.browser.fact

enum class FactVerdict { TRUE, FALSE, UNKNOWN }

enum class ResultOrigin { LOCAL_HEURISTIC, EXTERNAL_SOURCE, BOTH }

enum class EvidenceStance { SUPPORTS_TRUE, SUPPORTS_FALSE, NEUTRAL }

enum class EvidenceProvenance { PAGE_CONTENT, INDEPENDENT_SOURCE, LOCAL_VERIFIED_DATABASE }

data class FactSource(
    val name: String,
    val url: String,
    val publishedAt: String? = null,
    val sourceType: String? = null
)

data class StructuredFactCheck(
    val claim: String,
    val normalizedRating: Double,
    val reviewer: String?,
    val sourceUrl: String?
)

data class ArticleInput(
    val title: String,
    val content: String,
    val url: String,
    val domain: String,
    val publishedAt: String? = null,
    val author: String? = null,
    val citedSources: List<FactSource> = emptyList(),
    val structuredFactChecks: List<StructuredFactCheck> = emptyList()
)

data class ClickbaitResult(
    val score: Double,
    val reasons: List<String>
)

data class FactEvidence(
    val description: String,
    val stance: EvidenceStance,
    val strength: Double,
    val decisive: Boolean = false,
    val provenance: EvidenceProvenance = EvidenceProvenance.PAGE_CONTENT,
    val source: FactSource? = null
)

data class FactResult(
    val verdict: FactVerdict,
    val confidence: Double,
    val rationale: String,
    val evidence: List<FactEvidence>,
    val sources: List<FactSource>,
    val origin: ResultOrigin,
    val clickbait: ClickbaitResult,
    val claims: List<pl.sael.browser.fact.claim.Claim> = emptyList(),
    val evidenceSets: List<pl.sael.browser.fact.evidence.EvidenceSet> = emptyList()
)

interface ArticleExtractor {
    fun extract(html: String, url: String): ArticleInput
}

interface ClickbaitAnalyzer {
    fun analyze(title: String, content: String): ClickbaitResult
}

interface FactEvidenceProvider {
    val origin: ResultOrigin
    fun collect(article: ArticleInput, clickbait: ClickbaitResult): List<FactEvidence>
}

interface FactEngine {
    fun evaluate(article: ArticleInput): FactResult
}
