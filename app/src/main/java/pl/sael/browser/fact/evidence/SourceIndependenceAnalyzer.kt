package pl.sael.browser.fact.evidence

import java.net.URI
import java.security.MessageDigest
import java.util.Locale

class SourceIndependenceAnalyzer {
    fun cluster(items: List<EvidenceItem>): List<EvidenceCluster> {
        val valid = items.filter(::isValid)
        val parent = IntArray(valid.size) { it }
        fun root(index: Int): Int {
            var current = index
            while (parent[current] != current) {
                parent[current] = parent[parent[current]]
                current = parent[current]
            }
            return current
        }
        fun union(first: Int, second: Int) {
            val firstRoot = root(first)
            val secondRoot = root(second)
            if (firstRoot != secondRoot) parent[secondRoot] = firstRoot
        }
        valid.indices.forEach { first ->
            ((first + 1) until valid.size).forEach { second ->
                if (related(valid[first], valid[second])) union(first, second)
            }
        }
        return valid.indices.groupBy(::root).values.map { indices ->
            val members = indices.map(valid::get)
            EvidenceCluster(clusterId(members.first()), members)
        }
    }

    private fun isValid(item: EvidenceItem): Boolean = item.claimId.isNotBlank() &&
        item.summary.isNotBlank() && item.url.startsWith("https://") &&
        item.domain.isNotBlank() && item.confidence in 0.0..1.0 &&
        item.provenance != EvidenceProvenance.PAGE_CONTENT

    private fun related(a: EvidenceItem, b: EvidenceItem): Boolean {
        if (a.primarySourceId != null && a.primarySourceId == b.primarySourceId) return true
        if (canonicalDomain(a) == canonicalDomain(b)) return true
        return similarity(tokens(a.summary), tokens(b.summary)) >= SIMILARITY_THRESHOLD
    }

    private fun canonicalDomain(item: EvidenceItem): String = runCatching {
        (URI(item.url).host ?: item.domain).lowercase(Locale.ROOT).removePrefix("www.")
    }.getOrDefault(item.domain.lowercase(Locale.ROOT).removePrefix("www."))

    private fun tokens(text: String): Set<String> = text.lowercase(Locale.ROOT)
        .split(Regex("[^\\p{L}\\p{N}]+"))
        .filter { it.length >= 4 }.toSet()

    private fun similarity(a: Set<String>, b: Set<String>): Double =
        if (a.isEmpty() || b.isEmpty()) 0.0 else a.intersect(b).size.toDouble() / a.union(b).size

    private fun clusterId(item: EvidenceItem): String = MessageDigest.getInstance("SHA-256")
        .digest((item.primarySourceId ?: canonicalDomain(item) + ":" + item.summary.lowercase()).toByteArray())
        .take(6).joinToString("") { "%02x".format(it) }

    companion object { private const val SIMILARITY_THRESHOLD = 0.72 }
}
