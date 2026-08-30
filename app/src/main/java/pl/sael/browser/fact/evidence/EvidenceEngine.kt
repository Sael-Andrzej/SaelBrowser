package pl.sael.browser.fact.evidence

import pl.sael.browser.fact.claim.Claim
import pl.sael.browser.fact.claim.ClaimType
import pl.sael.browser.fact.trust.SourceTrustPolicy
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.TimeoutException

class EvidenceEngine(
    private val providers: List<EvidenceProvider>,
    private val independence: SourceIndependenceAnalyzer = SourceIndependenceAnalyzer(),
    private val temporal: TemporalRelevanceAnalyzer = TemporalRelevanceAnalyzer(),
    private val trust: SourceTrustPolicy = SourceTrustPolicy(),
    private val threshold: Double = 0.8,
    private val providerTimeoutMillis: Long = DEFAULT_PROVIDER_TIMEOUT_MILLIS
) {
    init {
        require(providerTimeoutMillis > 0)
    }

    fun evaluate(claim: Claim, articleUrl: String, articleDomain: String): EvidenceSet {
        if (claim.type != ClaimType.FACTUAL) return emptySet(claim, "Twierdzenie nie jest faktem nadającym się do oceny.")
        val errors = mutableListOf<String>()
        val executor = Executors.newFixedThreadPool(providers.size.coerceAtLeast(1).coerceAtMost(4))
        val items = try {
            providers.map { provider -> provider to executor.submit<List<EvidenceItem>> {
                provider.findEvidence(EvidenceQuery(claim, articleUrl, articleDomain))
            } }.flatMap { (provider, future) ->
                try {
                    future.get(providerTimeoutMillis, TimeUnit.MILLISECONDS)
                } catch (_: TimeoutException) {
                    future.cancel(true)
                    errors += "${provider.id}: timeout"
                    emptyList()
                } catch (error: Exception) {
                    errors += "${provider.id}: ${error.cause?.javaClass?.simpleName ?: error.javaClass.simpleName}"
                    emptyList()
                }
            }
        } finally {
            executor.shutdownNow()
        }.filter { it.claimId == claim.id }
        val clusters = independence.cluster(items)
        val supports = clusters.filter { clusterStance(it) == EvidenceStance.SUPPORTS }
        val refutes = clusters.filter { clusterStance(it) == EvidenceStance.REFUTES }
        val supportScore = combinedScore(supports, claim)
        val refuteScore = combinedScore(refutes, claim)
        val conflict = hasCredibleConflict(clusters, claim)
        val winning = maxOf(supportScore, refuteScore)
        val winningClusters = if (supportScore >= refuteScore) supports else refutes
        val hasIndependentBasis = winningClusters.size >= MIN_INDEPENDENT_CLUSTERS
        val sufficient = !conflict && hasIndependentBasis && winning >= threshold
        val message = when {
            conflict -> "Źródła są sprzeczne."
            clusters.isEmpty() -> "Brak wystarczających dowodów."
            !hasIndependentBasis -> "Źródła nie są wystarczająco niezależne."
            !sufficient -> "Dowody nie przekroczyły bezpiecznego progu."
            else -> "Niezależne dowody przekroczyły bezpieczny próg."
        }
        return EvidenceSet(claim, items, clusters, supports, refutes, conflict,
            if (conflict) 0.0 else winning.coerceIn(0.0, 1.0), sufficient, message, errors)
    }

    private fun combinedScore(clusters: List<EvidenceCluster>, claim: Claim): Double {
        val scores = clusters.map { clusterScore(it, claim) }.sortedDescending()
        if (scores.isEmpty()) return 0.0
        // Diminishing returns: quantity cannot turn weak sources into certainty.
        val strongest = scores.first()
        if (strongest < WEAK_SOURCE_CEILING) return strongest.coerceAtMost(WEAK_RESULT_CAP)
        return (strongest + (scores.drop(1).firstOrNull() ?: 0.0) * 0.18).coerceAtMost(0.98)
    }

    private fun clusterScore(cluster: EvidenceCluster, claim: Claim): Double = cluster.items.maxOfOrNull {
        trust.quality(it, claim) * temporal.score(claim, it)
    } ?: 0.0

    private fun clusterStance(cluster: EvidenceCluster): EvidenceStance {
        val stances = cluster.items.map(EvidenceItem::stance).toSet()
        return when {
            EvidenceStance.SUPPORTS in stances && EvidenceStance.REFUTES in stances -> EvidenceStance.UNKNOWN
            EvidenceStance.SUPPORTS in stances -> EvidenceStance.SUPPORTS
            EvidenceStance.REFUTES in stances -> EvidenceStance.REFUTES
            EvidenceStance.NEUTRAL in stances -> EvidenceStance.NEUTRAL
            else -> EvidenceStance.UNKNOWN
        }
    }

    private fun hasCredibleConflict(clusters: List<EvidenceCluster>, claim: Claim): Boolean {
        val credibleItems = clusters.flatMap(EvidenceCluster::items).filter {
            trust.quality(it, claim) * temporal.score(claim, it) >= CONFLICT_MIN
        }
        return credibleItems.any { it.stance == EvidenceStance.SUPPORTS } &&
            credibleItems.any { it.stance == EvidenceStance.REFUTES }
    }

    private fun emptySet(claim: Claim, message: String) = EvidenceSet(
        claim, emptyList(), emptyList(), emptyList(), emptyList(), false, 0.0, false, message
    )

    companion object {
        private const val MIN_INDEPENDENT_CLUSTERS = 2
        private const val CONFLICT_MIN = 0.45
        private const val WEAK_SOURCE_CEILING = 0.5
        private const val WEAK_RESULT_CAP = 0.49
        private const val DEFAULT_PROVIDER_TIMEOUT_MILLIS = 2_500L
    }
}
