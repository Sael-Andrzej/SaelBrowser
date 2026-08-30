package pl.sael.browser.fact.evidence

import pl.sael.browser.fact.claim.Claim

enum class EvidenceStance { SUPPORTS, REFUTES, NEUTRAL, UNKNOWN }
enum class SourceType { PRIMARY_OFFICIAL, PRIMARY_DOCUMENT, FACT_CHECK, NEWS_REPORT, ACADEMIC, SECONDARY, USER_GENERATED, UNKNOWN }
enum class EvidenceProvenance { EXTERNAL_API, VERIFIED_DATABASE, WEB_SEARCH, TEST_FAKE, PAGE_CONTENT }

data class EvidenceQuery(val claim: Claim, val articleUrl: String, val articleDomain: String)

data class EvidenceItem(
    val claimId: String,
    val summary: String,
    val url: String,
    val domain: String,
    val publisher: String,
    val author: String? = null,
    val publicationDate: String? = null,
    val eventDate: String? = null,
    val sourceType: SourceType = SourceType.UNKNOWN,
    val stance: EvidenceStance = EvidenceStance.UNKNOWN,
    val confidence: Double = 0.0,
    val provenance: EvidenceProvenance,
    val primarySourceId: String? = null,
    val direct: Boolean = false
)

data class EvidenceCluster(val id: String, val items: List<EvidenceItem>)

data class EvidenceSet(
    val claim: Claim,
    val items: List<EvidenceItem>,
    val clusters: List<EvidenceCluster>,
    val supports: List<EvidenceCluster>,
    val refutes: List<EvidenceCluster>,
    val conflict: Boolean,
    val confidence: Double,
    val sufficient: Boolean,
    val message: String,
    val providerErrors: List<String> = emptyList()
)

interface EvidenceProvider {
    val id: String
    fun findEvidence(query: EvidenceQuery): List<EvidenceItem>
}
