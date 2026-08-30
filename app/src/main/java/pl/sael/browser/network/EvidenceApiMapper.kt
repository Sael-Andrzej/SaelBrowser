package pl.sael.browser.network

import com.google.gson.JsonObject
import com.google.gson.JsonParser
import pl.sael.browser.fact.evidence.EvidenceItem
import pl.sael.browser.fact.evidence.EvidenceProvenance
import pl.sael.browser.fact.evidence.EvidenceStance
import pl.sael.browser.fact.evidence.SourceType
import java.net.URI
import java.text.Normalizer

class EvidenceApiMapper {
    fun map(body: String, expectedClaimId: String, expectedClaimText: String): List<EvidenceItem> {
        val root = runCatching { JsonParser.parseString(body).asJsonObject }.getOrNull() ?: return emptyList()
        val responseClaim = runCatching { root.string("query", 500) }.getOrNull() ?: return emptyList()
        if (normalizeClaim(responseClaim) != normalizeClaim(expectedClaimText)) return emptyList()
        val array = root.getAsJsonArray("evidence") ?: return emptyList()
        return array.take(MAX_ITEMS).mapNotNull { element ->
            runCatching { mapItem(element.asJsonObject, expectedClaimId) }.getOrNull()
        }
    }

    private fun mapItem(json: JsonObject, claimId: String): EvidenceItem? {
        val summary = json.string("snippet", 2_000)
        val url = json.string("url", 2_048)
        val uri = URI(url)
        if (uri.scheme != "https" || uri.userInfo != null || uri.host.isNullOrBlank()) return null
        val actualDomain = uri.host.lowercase().removePrefix("www.")
        if (!isPublicHostname(actualDomain)) return null
        val declaredDomain = json.string("domain", 253).lowercase().removePrefix("www.")
        if (declaredDomain != actualDomain) return null
        val confidence = json.number("providerConfidence")
        if (!confidence.isFinite() || confidence !in 0.0..1.0) return null
        val sourceType = enumValueOrNull<SourceType>(json.string("sourceType", 64)) ?: return null
        val stance = enumValueOrNull<EvidenceStance>(json.string("stance", 32)) ?: return null
        val provider = json.string("provider", 100)
        val declaredProvenance = json.string("provenance", 100)
        val validProviderContract = when (declaredProvenance) {
            "GOOGLE_FACT_CHECK" -> provider == "google-fact-check" &&
                sourceType == SourceType.FACT_CHECK && stance == EvidenceStance.UNKNOWN && confidence <= 0.8
            "BRAVE_SEARCH" -> provider == "brave-search" &&
                sourceType == SourceType.UNKNOWN && stance == EvidenceStance.UNKNOWN && confidence <= 0.5
            else -> false
        }
        if (!validProviderContract) return null
        val provenance = when (declaredProvenance) {
            "GOOGLE_FACT_CHECK", "BRAVE_SEARCH" -> EvidenceProvenance.EXTERNAL_API
            else -> return null
        }
        return EvidenceItem(
            claimId = claimId,
            summary = summary,
            url = url,
            domain = actualDomain,
            publisher = json.string("publisher", 300),
            author = json.optionalString("author", 300),
            publicationDate = json.optionalString("publishedAt", 32),
            eventDate = json.optionalString("eventDate", 32),
            sourceType = sourceType,
            stance = stance,
            confidence = confidence,
            provenance = provenance,
            primarySourceId = if (declaredProvenance == "GOOGLE_FACT_CHECK") url else null,
            direct = false
        )
    }

    private fun JsonObject.string(name: String, max: Int): String {
        val value = get(name)?.takeUnless { it.isJsonNull }?.asString?.trim().orEmpty()
        require(value.isNotEmpty() && value.length <= max)
        return value
    }

    private fun JsonObject.optionalString(name: String, max: Int): String? =
        get(name)?.takeUnless { it.isJsonNull }?.asString?.trim()?.takeIf { it.isNotEmpty() && it.length <= max }

    private fun JsonObject.number(name: String): Double = get(name)?.asDouble ?: Double.NaN

    private inline fun <reified T : Enum<T>> enumValueOrNull(value: String): T? =
        enumValues<T>().firstOrNull { it.name == value }

    private fun isPublicHostname(host: String): Boolean {
        if (host == "localhost" || host.endsWith(".localhost") || ':' in host) return false
        if (host.all(Char::isDigit) || host.startsWith("0x", ignoreCase = true)) return false
        val octets = host.split('.').mapNotNull(String::toIntOrNull)
        if (octets.size != 4) return true
        if (octets.any { it !in 0..255 }) return false
        return !(octets[0] == 10 || octets[0] == 127 || octets[0] == 0 ||
            octets[0] >= 224 || (octets[0] == 169 && octets[1] == 254) ||
            (octets[0] == 172 && octets[1] in 16..31) ||
            (octets[0] == 192 && octets[1] == 168))
    }

    private fun normalizeClaim(value: String): String = Normalizer.normalize(value, Normalizer.Form.NFKC)
        .replace(Regex("\\s+"), " ")
        .trim()

    companion object { private const val MAX_ITEMS = 10 }
}
