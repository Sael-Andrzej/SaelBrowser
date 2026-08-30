package pl.sael.browser.fact.providers

import pl.sael.browser.fact.evidence.EvidenceItem
import pl.sael.browser.fact.evidence.EvidenceProvider
import pl.sael.browser.fact.evidence.EvidenceQuery

/** Contract for a future legal search API or SAEL backend; no HTML scraping. */
interface WebSearchClient { fun search(query: String): List<EvidenceItem> }

class WebSearchProvider(private val client: WebSearchClient? = null) : EvidenceProvider {
    override val id = "web-search"
    override fun findEvidence(query: EvidenceQuery): List<EvidenceItem> =
        client?.search(query.claim.text).orEmpty().filter { it.claimId == query.claim.id }
}
