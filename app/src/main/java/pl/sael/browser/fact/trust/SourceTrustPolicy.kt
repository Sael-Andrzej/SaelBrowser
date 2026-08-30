package pl.sael.browser.fact.trust

import pl.sael.browser.fact.claim.Claim
import pl.sael.browser.fact.evidence.EvidenceItem
import pl.sael.browser.fact.evidence.SourceType

/** Quality is contextual; this is not a domain allow-list. */
class SourceTrustPolicy {
    fun quality(item: EvidenceItem, claim: Claim): Double {
        val typeWeight = when (item.sourceType) {
            SourceType.PRIMARY_OFFICIAL -> 0.9
            SourceType.PRIMARY_DOCUMENT -> 0.92
            SourceType.FACT_CHECK -> 0.86
            SourceType.ACADEMIC -> 0.9
            SourceType.NEWS_REPORT -> 0.68
            SourceType.SECONDARY -> 0.5
            SourceType.USER_GENERATED -> 0.25
            SourceType.UNKNOWN -> 0.3
        }
        val directness = if (item.direct) 1.0 else 0.82
        val numericSpecificity = if (claim.numbers.isNotEmpty() && item.sourceType in setOf(
                SourceType.PRIMARY_OFFICIAL, SourceType.PRIMARY_DOCUMENT, SourceType.ACADEMIC
            )) 1.0 else 0.95
        return (typeWeight * directness * numericSpecificity * item.confidence).coerceIn(0.0, 1.0)
    }
}
