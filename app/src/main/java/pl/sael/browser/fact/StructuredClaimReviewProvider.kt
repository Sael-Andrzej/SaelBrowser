package pl.sael.browser.fact

class StructuredClaimReviewProvider : FactEvidenceProvider {
    override val origin: ResultOrigin = ResultOrigin.LOCAL_HEURISTIC

    override fun collect(article: ArticleInput, clickbait: ClickbaitResult): List<FactEvidence> =
        article.structuredFactChecks.map { check ->
            val source = check.sourceUrl?.let { url ->
                FactSource(check.reviewer ?: "Deklarowane źródło ClaimReview", url)
            }
            FactEvidence(
                description = "Strona deklaruje ocenę ClaimReview dla twierdzenia „${check.claim}”, " +
                    "ale nie została ona niezależnie pobrana ani uwierzytelniona.",
                stance = EvidenceStance.NEUTRAL,
                strength = 0.4,
                decisive = false,
                provenance = EvidenceProvenance.PAGE_CONTENT,
                source = source
            )
        }
}
