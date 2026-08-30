package pl.sael.browser.fact.providers

import pl.sael.browser.fact.evidence.EvidenceItem
import pl.sael.browser.fact.evidence.EvidenceProvider
import pl.sael.browser.fact.evidence.EvidenceQuery

interface FactCheckApiClient { fun search(query: String): List<FactCheckRecord> }

data class FactCheckRecord(
    val claim: String, val summary: String, val url: String, val publisher: String,
    val reviewDate: String?, val supports: Boolean?, val confidence: Double
)

/** Adapter boundary for a documented API such as Google Fact Check Tools.
 * Production intentionally has no client/key and safely returns no evidence.
 */
class FactCheckApiProvider(private val client: FactCheckApiClient? = null) : EvidenceProvider {
    override val id = "fact-check-api"
    override fun findEvidence(query: EvidenceQuery): List<EvidenceItem> {
        val api = client ?: return emptyList()
        return api.search(query.claim.text).mapNotNull { record ->
            val stance = when (record.supports) {
                true -> pl.sael.browser.fact.evidence.EvidenceStance.SUPPORTS
                false -> pl.sael.browser.fact.evidence.EvidenceStance.REFUTES
                null -> pl.sael.browser.fact.evidence.EvidenceStance.UNKNOWN
            }
            val domain = runCatching { java.net.URI(record.url).host.orEmpty() }.getOrDefault("")
            if (!record.url.startsWith("https://") || domain.isBlank()) return@mapNotNull null
            EvidenceItem(query.claim.id, record.summary, record.url, domain, record.publisher,
                publicationDate = record.reviewDate,
                sourceType = pl.sael.browser.fact.evidence.SourceType.FACT_CHECK,
                stance = stance, confidence = record.confidence,
                provenance = pl.sael.browser.fact.evidence.EvidenceProvenance.EXTERNAL_API,
                primarySourceId = record.url, direct = true)
        }
    }
}
