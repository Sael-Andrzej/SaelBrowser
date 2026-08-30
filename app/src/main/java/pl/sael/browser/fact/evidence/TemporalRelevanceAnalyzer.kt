package pl.sael.browser.fact.evidence

import pl.sael.browser.fact.claim.Claim
import java.time.LocalDate
import java.time.format.DateTimeParseException
import java.time.temporal.ChronoUnit

class TemporalRelevanceAnalyzer(private val now: LocalDate = LocalDate.now()) {
    fun score(claim: Claim, item: EvidenceItem): Double {
        val claimDate = parse(claim.claimDate) ?: now
        val evidenceDate = parse(item.eventDate) ?: parse(item.publicationDate) ?: return 0.55
        val days = kotlin.math.abs(ChronoUnit.DAYS.between(claimDate, evidenceDate))
        return when {
            days <= 31 -> 1.0
            days <= 183 -> 0.85
            days <= 366 -> 0.65
            else -> 0.25
        }
    }

    private fun parse(value: String?): LocalDate? = if (value.isNullOrBlank()) null else try {
        LocalDate.parse(value.take(10))
    } catch (_: DateTimeParseException) { null }
}
