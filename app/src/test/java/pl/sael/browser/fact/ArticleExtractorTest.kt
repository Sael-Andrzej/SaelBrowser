package pl.sael.browser.fact

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ArticleExtractorTest {
    private val extractor = JsoupArticleExtractor()

    @Test
    fun `extracts article and ignores navigation footer comments and ads`() {
        val html = """
            <html><head>
              <title>Tytuł zapasowy</title>
              <meta property="og:title" content="Właściwy tytuł artykułu">
              <meta name="author" content="Jan Kowalski">
              <meta property="article:published_time" content="2026-08-30">
            </head><body>
              <nav>MENU TAJNE</nav>
              <article>
                <h1>Właściwy tytuł artykułu</h1>
                <p>${"Główna treść artykułu opisuje sprawę i podaje konkretne informacje. ".repeat(5)}</p>
                <a href="https://source.example/report">Raport źródłowy</a>
                <div class="comments">KOMENTARZ TAJNY</div>
                <div class="adsbygoogle">REKLAMA TAJNA</div>
              </article>
              <footer>STOPKA TAJNA</footer>
            </body></html>
        """.trimIndent()

        val article = extractor.extract(html, "https://news.example/post")
        assertEquals("Właściwy tytuł artykułu", article.title)
        assertEquals("Jan Kowalski", article.author)
        assertEquals("2026-08-30", article.publishedAt)
        assertEquals("news.example", article.domain)
        assertTrue(article.content.contains("Główna treść"))
        assertFalse(article.content.contains("MENU TAJNE"))
        assertFalse(article.content.contains("KOMENTARZ TAJNY"))
        assertFalse(article.content.contains("REKLAMA TAJNA"))
        assertFalse(article.content.contains("STOPKA TAJNA"))
        assertEquals("https://source.example/report", article.citedSources.single().url)
    }

    @Test
    fun `extracts numeric claim review without interpreting rating words`() {
        val html = """
            <html><body><article><h1>Ocena twierdzenia testowego</h1>
            <p>${"Opis analizowanego twierdzenia oraz metodologii weryfikacji. ".repeat(4)}</p></article>
            <script type="application/ld+json">
            {
              "@type":"ClaimReview",
              "claimReviewed":"Twierdzenie testowe",
              "url":"https://facts.example/check/1",
              "author":{"name":"Redakcja testowa"},
              "reviewRating":{"ratingValue":5,"bestRating":5,"worstRating":1}
            }
            </script></body></html>
        """.trimIndent()
        val article = extractor.extract(html, "https://facts.example/check/1")
        val check = article.structuredFactChecks.single()
        assertEquals("Twierdzenie testowe", check.claim)
        assertEquals(1.0, check.normalizedRating, 0.0001)
        assertEquals("Redakcja testowa", check.reviewer)
    }

    @Test
    fun `handles empty and damaged content`() {
        val empty = extractor.extract("", "not a valid url")
        assertEquals("", empty.title)
        assertEquals("", empty.content)
        assertNull(empty.author)

        val damaged = extractor.extract("<html><article><h1>Urwany", "https://example.com")
        assertTrue(damaged.content.isEmpty() || damaged.content.contains("Urwany"))
    }
}
