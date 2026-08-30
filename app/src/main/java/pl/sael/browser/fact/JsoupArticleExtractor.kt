package pl.sael.browser.fact

import com.google.gson.JsonElement
import com.google.gson.JsonObject
import com.google.gson.JsonParser
import org.jsoup.Jsoup
import org.jsoup.nodes.Document
import org.jsoup.nodes.Element
import java.net.URI

class JsoupArticleExtractor : ArticleExtractor {
    override fun extract(html: String, url: String): ArticleInput {
        val domain = runCatching { URI(url).host.orEmpty().removePrefix("www.") }.getOrDefault("")
        if (html.isBlank()) return ArticleInput("", "", url, domain)

        val document = runCatching { Jsoup.parse(html, url) }.getOrElse {
            return ArticleInput("", "", url, domain)
        }
        val working = document.clone()
        working.select(EXCLUDED_SELECTORS).remove()

        val main = selectMainContent(working)
        val content = main?.text()?.normalizeWhitespace().orEmpty()
        val title = firstNonBlank(
            document.selectFirst("meta[property=og:title]")?.attr("content"),
            document.selectFirst("meta[name=twitter:title]")?.attr("content"),
            main?.selectFirst("h1")?.text(),
            document.selectFirst("h1")?.text(),
            document.title()
        )
        val author = firstNonBlank(
            document.selectFirst("meta[name=author]")?.attr("content"),
            document.selectFirst("meta[property=article:author]")?.attr("content"),
            main?.selectFirst("[rel=author], [itemprop=author]")?.text()
        ).ifBlank { null }
        val publishedAt = firstNonBlank(
            document.selectFirst("meta[property=article:published_time]")?.attr("content"),
            document.selectFirst("meta[itemprop=datePublished]")?.attr("content"),
            main?.selectFirst("time[datetime]")?.attr("datetime")
        ).ifBlank { null }

        return ArticleInput(
            title = title.normalizeWhitespace(),
            content = content,
            url = url,
            domain = domain,
            publishedAt = publishedAt,
            author = author,
            citedSources = extractSources(main, domain),
            structuredFactChecks = extractClaimReviews(document, url)
        )
    }

    private fun selectMainContent(document: Document): Element? {
        val preferred = document.select("article, main, [role=main], [itemprop=articleBody]")
        val candidates = if (preferred.isNotEmpty()) preferred else document.select("section, div")
        return candidates
            .asSequence()
            .map { element -> element to contentScore(element) }
            .filter { (_, score) -> score > 100 }
            .maxByOrNull { (_, score) -> score }
            ?.first
            ?: document.body()?.takeIf { it.text().length >= 80 }
    }

    private fun contentScore(element: Element): Int {
        val textLength = element.ownText().length + element.select("p").sumOf { it.text().length }
        val linkLength = element.select("a").sumOf { it.text().length }
        val semanticBonus = when (element.tagName()) {
            "article" -> 500
            "main" -> 300
            else -> if (element.hasAttr("itemprop")) 250 else 0
        }
        return textLength - (linkLength * 2) + (element.select("p").size * 60) + semanticBonus
    }

    private fun extractSources(main: Element?, articleDomain: String): List<FactSource> {
        if (main == null) return emptyList()
        return main.select("a[href]")
            .asSequence()
            .mapNotNull { link ->
                val href = link.absUrl("href")
                val host = runCatching { URI(href).host.orEmpty().removePrefix("www.") }.getOrDefault("")
                if (href.isBlank() || host.isBlank() || host == articleDomain) null
                else FactSource(link.text().ifBlank { host }, href)
            }
            .distinctBy(FactSource::url)
            .take(20)
            .toList()
    }

    private fun extractClaimReviews(document: Document, pageUrl: String): List<StructuredFactCheck> {
        val checks = mutableListOf<StructuredFactCheck>()
        document.select("script[type=application/ld+json]").forEach { script ->
            runCatching { JsonParser.parseString(script.data()) }
                .getOrNull()
                ?.let { root -> visitJson(root) { obj -> parseClaimReview(obj, pageUrl)?.let(checks::add) } }
        }
        return checks.distinctBy { listOf(it.claim, it.normalizedRating, it.sourceUrl) }
    }

    private fun visitJson(element: JsonElement, visitor: (JsonObject) -> Unit) {
        when {
            element.isJsonObject -> {
                val obj = element.asJsonObject
                visitor(obj)
                obj.entrySet().forEach { (_, child) -> visitJson(child, visitor) }
            }
            element.isJsonArray -> element.asJsonArray.forEach { visitJson(it, visitor) }
        }
    }

    private fun parseClaimReview(obj: JsonObject, pageUrl: String): StructuredFactCheck? {
        if (!hasType(obj, "ClaimReview")) return null
        val rating = obj.getAsJsonObject("reviewRating") ?: return null
        val value = rating.number("ratingValue") ?: return null
        val best = rating.number("bestRating") ?: 5.0
        val worst = rating.number("worstRating") ?: 1.0
        if (best <= worst || value !in worst..best) return null

        val claim = obj.getAsJsonObject("itemReviewed")?.string("name")
            ?: obj.string("claimReviewed")
            ?: return null
        val reviewer = obj.getAsJsonObject("author")?.string("name")
        val sourceUrl = obj.string("url") ?: pageUrl
        return StructuredFactCheck(
            claim = claim,
            normalizedRating = ((value - worst) / (best - worst)).coerceIn(0.0, 1.0),
            reviewer = reviewer,
            sourceUrl = sourceUrl
        )
    }

    private fun hasType(obj: JsonObject, expected: String): Boolean {
        val type = obj.get("@type") ?: return false
        return when {
            type.isJsonPrimitive -> type.asString == expected
            type.isJsonArray -> type.asJsonArray.any { it.isJsonPrimitive && it.asString == expected }
            else -> false
        }
    }

    private fun JsonObject.string(name: String): String? = get(name)
        ?.takeIf { it.isJsonPrimitive }
        ?.asString
        ?.trim()
        ?.takeIf(String::isNotEmpty)

    private fun JsonObject.number(name: String): Double? = get(name)
        ?.takeIf { it.isJsonPrimitive }
        ?.asJsonPrimitive
        ?.takeIf { it.isNumber }
        ?.asDouble

    private fun firstNonBlank(vararg values: String?): String = values.firstOrNull { !it.isNullOrBlank() }.orEmpty()
    private fun String.normalizeWhitespace(): String = replace(Regex("\\s+"), " ").trim()

    companion object {
        private const val EXCLUDED_SELECTORS =
            "nav, footer, aside, form, noscript, script, style, " +
                "[role=navigation], [role=complementary], [data-sael-hidden=true], " +
                ".comments, .comment, #comments, [class*=comment-list], " +
                ".adsbygoogle, [data-ad-slot], [id^=google_ads_], [id*=div-gpt-ad]"
    }
}
