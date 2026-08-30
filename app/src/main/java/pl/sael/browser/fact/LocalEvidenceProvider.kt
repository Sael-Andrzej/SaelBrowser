package pl.sael.browser.fact

class LocalEvidenceProvider : FactEvidenceProvider {
    override val origin: ResultOrigin = ResultOrigin.LOCAL_HEURISTIC

    override fun collect(article: ArticleInput, clickbait: ClickbaitResult): List<FactEvidence> {
        val evidence = mutableListOf<FactEvidence>()
        if (article.title.isBlank() || article.content.length < MIN_ARTICLE_LENGTH) {
            evidence += FactEvidence(
                "Brak wystarczającej treści artykułu do rzetelnej analizy.",
                EvidenceStance.NEUTRAL,
                1.0
            )
            return evidence
        }

        if (article.citedSources.isNotEmpty()) {
            evidence += FactEvidence(
                "Artykuł zawiera ${article.citedSources.size} odnośników do zewnętrznych źródeł.",
                EvidenceStance.NEUTRAL,
                0.35
            )
        } else {
            evidence += FactEvidence(
                "W głównej treści nie znaleziono odnośników do zewnętrznych źródeł.",
                EvidenceStance.NEUTRAL,
                0.25
            )
        }

        if (clickbait.score >= 0.6) {
            evidence += FactEvidence(
                "Tytuł ma silne cechy clickbaitu; nie jest to dowód fałszu.",
                EvidenceStance.NEUTRAL,
                clickbait.score
            )
        }

        detectNumericContradiction(article.content)?.let { contradiction ->
            evidence += FactEvidence(
                contradiction,
                EvidenceStance.NEUTRAL,
                0.65
            )
        }
        return evidence
    }

    private fun detectNumericContradiction(content: String): String? {
        val statements = NUMERIC_STATEMENT.findAll(content.take(30_000))
            .map { match ->
                match.groupValues[1].lowercase().replace(Regex("\\s+"), " ").trim() to
                    match.groupValues[2].replace(" ", "")
            }
            .toList()
        val conflict = statements.groupBy({ it.first }, { it.second })
            .entries.firstOrNull { (_, values) -> values.distinct().size > 1 }
        return conflict?.let {
            "W treści znaleziono niespójne wartości liczbowe dla podobnego twierdzenia: ${it.value.distinct().joinToString()}."
        }
    }

    companion object {
        private const val MIN_ARTICLE_LENGTH = 120
        private val NUMERIC_STATEMENT = Regex(
            "(?i)([\\p{L}][\\p{L}\\s]{4,60}?(?:wynosi|to|jest|equals|is))\\s+([0-9][0-9 ]*(?:[.,][0-9]+)?)"
        )
    }
}
