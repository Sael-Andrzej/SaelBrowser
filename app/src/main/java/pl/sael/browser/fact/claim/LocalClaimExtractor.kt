package pl.sael.browser.fact.claim

import java.security.MessageDigest
import java.util.Locale

/** Conservative, deterministic extraction. Article text is untrusted data, never instructions. */
class LocalClaimExtractor : ClaimExtractor {
    override fun extract(title: String, content: String, articleDate: String?): List<Claim> {
        val sentences = (sequenceOf(title) + content.splitToSequence(SENTENCE_BOUNDARY))
            .map { it.replace(Regex("\\s+"), " ").trim() }
            .filter { it.length in MIN_LENGTH..MAX_LENGTH }
            .distinct()
            .take(MAX_CLAIMS)
        return sentences.mapNotNull { sentence ->
            val type = classify(sentence)
            if (type == ClaimType.UNKNOWN && !hasFactualSignal(sentence)) return@mapNotNull null
            val numbers = NUMBER.findAll(sentence).map {
                ClaimNumber(it.groupValues[1], it.groupValues[2].ifBlank { null })
            }.toList()
            Claim(
                id = stableId(sentence),
                text = sentence,
                type = type,
                priority = priority(sentence, type, numbers),
                context = sentence,
                subject = SUBJECT.find(sentence)?.groupValues?.get(1)?.trim(),
                claimDate = DATE.find(sentence)?.value ?: articleDate,
                numbers = numbers
            )
        }.sortedByDescending(Claim::priority).toList()
    }

    private fun classify(text: String): ClaimType {
        val lower = text.lowercase(Locale.ROOT)
        if (OPINION.any(lower::contains)) return ClaimType.OPINION
        if (PREDICTION.any(lower::contains)) return ClaimType.PREDICTION
        return if (hasFactualSignal(text)) ClaimType.FACTUAL else ClaimType.UNKNOWN
    }

    private fun hasFactualSignal(text: String): Boolean =
        NUMBER.containsMatchIn(text) || DATE.containsMatchIn(text) ||
            FACT_VERBS.containsMatchIn(text)

    private fun priority(text: String, type: ClaimType, numbers: List<ClaimNumber>): Double = when (type) {
        ClaimType.FACTUAL -> (0.55 + if (numbers.isNotEmpty()) 0.2 else 0.0 +
            if (DATE.containsMatchIn(text)) 0.15 else 0.0).coerceAtMost(1.0)
        ClaimType.PREDICTION -> 0.35
        ClaimType.OPINION -> 0.2
        ClaimType.UNKNOWN -> 0.1
    }

    private fun stableId(text: String): String = MessageDigest.getInstance("SHA-256")
        .digest(text.lowercase(Locale.ROOT).toByteArray())
        .take(8).joinToString("") { "%02x".format(it) }

    companion object {
        private const val MIN_LENGTH = 18
        private const val MAX_LENGTH = 500
        private const val MAX_CLAIMS = 30
        private val SENTENCE_BOUNDARY = Regex("(?<=[.!?])\\s+")
        private val NUMBER = Regex("(?<!\\w)(\\d+(?:[.,]\\d+)?)(?:\\s*(%|proc\\.|zł|PLN|EUR|USD|km|kg|mln|mld))?", RegexOption.IGNORE_CASE)
        private val DATE = Regex("\\b(?:\\d{1,2}[.-]\\d{1,2}[.-]\\d{2,4}|\\d{4}-\\d{2}-\\d{2})\\b")
        private val FACT_VERBS = Regex("(?i)\\b(wynosi|ogłosił[ao]?|opublikował[ao]?|wystrzelił[ao]?|podpisał[ao]?|zmarł[ao]?|urodził[ao]?|jest|był[ao]?|ma|posiada|stwierdził[ao]?|reported|announced|is|was|has)\\b")
        private val SUBJECT = Regex("^([\\p{L}\\d][\\p{L}\\d .'-]{1,80}?)\\s+(?:wynosi|ogłosił|ogłosiła|jest|ma|reported|announced|is|has)\\b", RegexOption.IGNORE_CASE)
        private val OPINION = listOf("moim zdaniem", "uważam", "najlepszy", "najgorszy", "piękny", "fatalnie", "skandal")
        private val PREDICTION = listOf("prawdopodobnie", "może się", "przewiduje", "prognozuje", "będzie", "likely", "will ")
    }
}
