package pl.sael.browser.fact

class ThresholdFactEngine(
    private val clickbaitAnalyzer: ClickbaitAnalyzer = LocalClickbaitAnalyzer(),
    private val providers: List<FactEvidenceProvider> = listOf(
        LocalEvidenceProvider(),
        StructuredClaimReviewProvider()
    ),
    private val verdictThreshold: Double = DEFAULT_VERDICT_THRESHOLD,
    private val claimExtractor: pl.sael.browser.fact.claim.ClaimExtractor =
        pl.sael.browser.fact.claim.LocalClaimExtractor(),
    remoteEvidenceProvider: pl.sael.browser.fact.evidence.EvidenceProvider =
        pl.sael.browser.network.RemoteEvidenceProvider.configured(
            pl.sael.browser.BuildConfig.SAEL_BACKEND_URL,
            pl.sael.browser.BuildConfig.SAEL_BACKEND_FALLBACK_URL
        ),
    private val evidenceEngine: pl.sael.browser.fact.evidence.EvidenceEngine =
        pl.sael.browser.fact.evidence.EvidenceEngine(
            listOf(
                pl.sael.browser.fact.providers.FactCheckApiProvider(),
                pl.sael.browser.fact.providers.WebSearchProvider(),
                remoteEvidenceProvider
            )
        ),
    private val localOnlyEvidenceEngine: pl.sael.browser.fact.evidence.EvidenceEngine =
        pl.sael.browser.fact.evidence.EvidenceEngine(emptyList())
) : FactEngine {
    init {
        require(verdictThreshold in 0.5..1.0)
    }

    override fun evaluate(article: ArticleInput): FactResult {
        val clickbait = clickbaitAnalyzer.analyze(article.title, article.content)
        val claims = claimExtractor.extract(article.title, article.content, article.publishedAt)
        val remoteClaimIds = claims
            .filter { it.type == pl.sael.browser.fact.claim.ClaimType.FACTUAL }
            .take(MAX_REMOTE_CLAIMS)
            .map { it.id }
            .toSet()
        val evidenceSets = claims.map { claim ->
            val engine = if (claim.id in remoteClaimIds) evidenceEngine else localOnlyEvidenceEngine
            engine.evaluate(claim, article.url, article.domain)
        }
        val collected = providers.map { provider -> provider to provider.collect(article, clickbait) }
        val evidence = collected.flatMap { it.second }
        val trueEvidence = evidence.filter {
            it.decisive && it.provenance != EvidenceProvenance.PAGE_CONTENT &&
                it.stance == EvidenceStance.SUPPORTS_TRUE
        }
        val falseEvidence = evidence.filter {
            it.decisive && it.provenance != EvidenceProvenance.PAGE_CONTENT &&
                it.stance == EvidenceStance.SUPPORTS_FALSE
        }
        val trueConfidence = trueEvidence.maxOfOrNull(FactEvidence::strength) ?: 0.0
        val falseConfidence = falseEvidence.maxOfOrNull(FactEvidence::strength) ?: 0.0

        val verdict: FactVerdict
        val confidence: Double
        val rationale: String

        val externalConflicts = evidenceSets.any(pl.sael.browser.fact.evidence.EvidenceSet::conflict)
        val factualSets = evidenceSets.filter {
            it.claim.type == pl.sael.browser.fact.claim.ClaimType.FACTUAL
        }
        val hasUnresolvedFactualClaim = factualSets.any { !it.sufficient }
        val externalSupports = evidenceSets.filter {
            it.sufficient && it.supports.isNotEmpty() && it.refutes.isEmpty()
        }
        val externalRefutes = evidenceSets.filter {
            it.sufficient && it.refutes.isNotEmpty() && it.supports.isEmpty()
        }

        when {
            externalConflicts || (externalSupports.isNotEmpty() && externalRefutes.isNotEmpty()) -> {
                verdict = FactVerdict.UNKNOWN
                confidence = 0.0
                rationale = "Źródła są sprzeczne. Wyniku nie można uczciwie rozstrzygnąć."
            }
            externalSupports.isNotEmpty() && !hasUnresolvedFactualClaim -> {
                verdict = FactVerdict.TRUE
                confidence = externalSupports.maxOf(pl.sael.browser.fact.evidence.EvidenceSet::confidence)
                rationale = "Niezależne dowody zewnętrzne przekroczyły bezpieczny próg potwierdzenia."
            }
            externalRefutes.isNotEmpty() && !hasUnresolvedFactualClaim -> {
                verdict = FactVerdict.FALSE
                confidence = externalRefutes.maxOf(pl.sael.browser.fact.evidence.EvidenceSet::confidence)
                rationale = "Niezależne dowody zewnętrzne przekroczyły bezpieczny próg obalenia."
            }
            trueEvidence.isNotEmpty() && falseEvidence.isNotEmpty() -> {
                verdict = FactVerdict.UNKNOWN
                confidence = 0.0
                rationale = "Dostępne jednoznaczne przesłanki są ze sobą sprzeczne. Wyniku nie można rozstrzygnąć lokalnie."
            }
            trueConfidence >= verdictThreshold -> {
                verdict = FactVerdict.TRUE
                confidence = trueConfidence
                rationale = "Jednoznaczna przesłanka strukturalna przekroczyła bezpieczny próg potwierdzenia."
            }
            falseConfidence >= verdictThreshold -> {
                verdict = FactVerdict.FALSE
                confidence = falseConfidence
                rationale = "Jednoznaczna przesłanka strukturalna przekroczyła bezpieczny próg obalenia."
            }
            else -> {
                verdict = FactVerdict.UNKNOWN
                confidence = maxOf(trueConfidence, falseConfidence, neutralConfidence(evidence))
                rationale = if (trueEvidence.isNotEmpty() || falseEvidence.isNotEmpty()) {
                    "Przesłanki nie przekroczyły bezpiecznego progu ${formatThreshold(verdictThreshold)}."
                } else {
                    "Brak niezależnego, jednoznacznego dowodu pozwalającego uczciwie potwierdzić lub obalić twierdzenie."
                }
            }
        }

        val activeOrigins = collected.filter { it.second.isNotEmpty() }.map { it.first.origin }.toSet()
        val origin = when {
            ResultOrigin.EXTERNAL_SOURCE in activeOrigins && ResultOrigin.LOCAL_HEURISTIC in activeOrigins -> ResultOrigin.BOTH
            ResultOrigin.EXTERNAL_SOURCE in activeOrigins -> ResultOrigin.EXTERNAL_SOURCE
            else -> ResultOrigin.LOCAL_HEURISTIC
        }
        val sources = (article.citedSources + evidence.mapNotNull(FactEvidence::source))
            .distinctBy(FactSource::url)

        return FactResult(
            verdict = verdict,
            confidence = confidence.coerceIn(0.0, 1.0),
            rationale = rationale,
            evidence = evidence,
            sources = sources,
            origin = origin,
            clickbait = clickbait,
            claims = claims,
            evidenceSets = evidenceSets
        )
    }

    private fun neutralConfidence(evidence: List<FactEvidence>): Double =
        ((evidence.filter { !it.decisive }.maxOfOrNull(FactEvidence::strength) ?: 0.0) * 0.5)
            .coerceAtMost(UNKNOWN_CONFIDENCE_CAP)

    private fun formatThreshold(value: Double): String = "${(value * 100).toInt()}%"

    companion object {
        const val DEFAULT_VERDICT_THRESHOLD = 0.8
        private const val UNKNOWN_CONFIDENCE_CAP = 0.49
        private const val MAX_REMOTE_CLAIMS = 3
    }
}
