package pl.sael.browser.fact.claim

enum class ClaimType { FACTUAL, OPINION, PREDICTION, UNKNOWN }

data class ClaimNumber(val value: String, val unit: String? = null)

data class Claim(
    val id: String,
    val text: String,
    val type: ClaimType,
    val priority: Double,
    val context: String,
    val subject: String? = null,
    val claimDate: String? = null,
    val numbers: List<ClaimNumber> = emptyList()
)

interface ClaimExtractor {
    fun extract(title: String, content: String, articleDate: String? = null): List<Claim>
}
