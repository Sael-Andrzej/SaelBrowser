package pl.sael.browser.fact.evidence

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import pl.sael.browser.fact.claim.Claim
import pl.sael.browser.fact.claim.ClaimType
import pl.sael.browser.fact.claim.ClaimNumber

class EvidenceEngineTest {
    private val claim = Claim("c1", "Inflacja wynosi 3,1%.", ClaimType.FACTUAL, 0.9,
        "Inflacja wynosi 3,1%.", "Inflacja", "2026-08-30", listOf(ClaimNumber("3,1", "%")))

    @Test fun `no evidence returns unknown set`() {
        val set = EvidenceEngine(emptyList()).evaluate(claim, ARTICLE, "article.example")
        assertFalse(set.sufficient)
        assertEquals(0.0, set.confidence, 0.0)
    }

    @Test fun `one weak page remains insufficient`() {
        val set = engine(listOf(item("weak", "weak.example", SourceType.USER_GENERATED, 1.0)))
        assertFalse(set.sufficient)
        assertTrue(set.confidence <= 0.49)
    }

    @Test fun `ten syndicated copies form one evidence cluster`() {
        val copies = (1..10).map { index ->
            item("copy-$index", "portal$index.example", SourceType.NEWS_REPORT, 0.95,
                primary = "reuters-wire-123", summary = "Ten sam komunikat agencyjny podaje wynik inflacji 3,1 procent.")
        }
        val set = engine(copies)
        assertEquals(1, set.clusters.size)
        assertFalse(set.sufficient)
    }

    @Test fun `two independent strong direct documents are sufficient`() {
        val set = engine(listOf(
            item("a", "office.example", SourceType.PRIMARY_OFFICIAL, 0.98, summary = "Oficjalny odczyt inflacji wyniósł 3,1 procent."),
            item("b", "study.example", SourceType.ACADEMIC, 0.98, summary = "Niezależne badanie potwierdza wartość indeksu na poziomie 3,1 procent.")
        ))
        assertEquals(2, set.clusters.size)
        assertTrue(set.sufficient)
        assertTrue(set.confidence >= 0.8)
    }

    @Test fun `single strong primary source remains insufficient`() {
        val set = engine(listOf(
            item("only", "official.example", SourceType.PRIMARY_DOCUMENT, 1.0)
        ))
        assertFalse(set.sufficient)
        assertEquals(1, set.clusters.size)
    }

    @Test fun `supports and refutes creates unresolved conflict`() {
        val set = engine(listOf(
            item("a", "office.example", SourceType.PRIMARY_OFFICIAL, 0.98, EvidenceStance.SUPPORTS,
                summary = "Urząd statystyczny opublikował tabelę wskazującą 3,1 procent."),
            item("b", "document.example", SourceType.PRIMARY_DOCUMENT, 0.98, EvidenceStance.REFUTES,
                summary = "Dokument źródłowy zawiera przeciwny wynik 4,2 procent dla tego okresu.")
        ))
        assertTrue(set.conflict)
        assertFalse(set.sufficient)
        assertEquals(0.0, set.confidence, 0.0)
    }

    @Test fun `opposing evidence inside one syndicated cluster is still a conflict`() {
        val set = engine(listOf(
            item("support", "one.example", SourceType.PRIMARY_DOCUMENT, 0.98,
                EvidenceStance.SUPPORTS, primary = "shared-document"),
            item("refute", "two.example", SourceType.PRIMARY_DOCUMENT, 0.98,
                EvidenceStance.REFUTES, primary = "shared-document")
        ))
        assertEquals(1, set.clusters.size)
        assertTrue(set.conflict)
        assertFalse(set.sufficient)
    }

    @Test fun `old evidence cannot confirm current claim`() {
        val old = item("old", "office.example", SourceType.PRIMARY_OFFICIAL, 1.0).copy(
            publicationDate = "2019-01-01", eventDate = "2019-01-01"
        )
        val set = engine(listOf(old))
        assertFalse(set.sufficient)
        assertTrue(set.confidence < 0.5)
    }

    @Test fun `opinion and prediction are not evaluated`() {
        listOf(ClaimType.OPINION, ClaimType.PREDICTION).forEach { type ->
            val set = EvidenceEngine(listOf(provider(listOf(item("a", "a.example", SourceType.PRIMARY_DOCUMENT, 1.0)))))
                .evaluate(claim.copy(type = type), ARTICLE, "article.example")
            assertFalse(set.sufficient)
            assertTrue(set.items.isEmpty())
        }
    }

    @Test fun `invalid or page supplied evidence is discarded`() {
        val invalid = item("bad", "article.example", SourceType.FACT_CHECK, 1.0).copy(
            url = "", summary = "", provenance = EvidenceProvenance.PAGE_CONTENT
        )
        val set = engine(listOf(invalid))
        assertTrue(set.clusters.isEmpty())
        assertFalse(set.sufficient)
    }

    @Test fun `provider error safely produces unknown`() {
        val broken = object : EvidenceProvider {
            override val id = "timeout-provider"
            override fun findEvidence(query: EvidenceQuery): List<EvidenceItem> = throw RuntimeException("timeout")
        }
        val set = EvidenceEngine(listOf(broken)).evaluate(claim, ARTICLE, "article.example")
        assertFalse(set.sufficient)
        assertEquals(1, set.providerErrors.size)
    }

    @Test fun `provider timeout safely produces unknown`() {
        val slow = object : EvidenceProvider {
            override val id = "slow-provider"
            override fun findEvidence(query: EvidenceQuery): List<EvidenceItem> {
                Thread.sleep(5_000)
                return emptyList()
            }
        }
        val set = EvidenceEngine(listOf(slow), providerTimeoutMillis = 20)
            .evaluate(claim, ARTICLE, "article.example")
        assertFalse(set.sufficient)
        assertTrue(set.providerErrors.single().contains("timeout"))
    }

    @Test fun `many weak independent sources cannot pump confidence`() {
        val weak = (1..20).map { item("w$it", "weak$it.example", SourceType.USER_GENERATED, 1.0,
            summary = "Różna relacja użytkownika numer $it bez dokumentu pierwotnego.") }
        val set = engine(weak)
        assertFalse(set.sufficient)
        assertTrue(set.confidence <= 0.49)
    }

    @Test fun `same domain and same primary source are deduplicated`() {
        val items = listOf(
            item("1", "same.example", SourceType.NEWS_REPORT, 0.8, primary = "wire-1"),
            item("2", "same.example", SourceType.NEWS_REPORT, 0.8, primary = "wire-2"),
            item("3", "other.example", SourceType.NEWS_REPORT, 0.8, primary = "wire-1")
        )
        assertEquals(1, SourceIndependenceAnalyzer().cluster(items).size)
    }

    @Test fun `transitive source relationship merges clusters independent of input order`() {
        val first = item("first", "first.example", SourceType.NEWS_REPORT, 0.8,
            primary = "wire-a", summary = "Pierwsza całkowicie odmienna relacja źródłowa.")
        val third = item("third", "third.example", SourceType.NEWS_REPORT, 0.8,
            primary = "wire-b", summary = "Trzecia zupełnie osobna treść dokumentu.")
        val bridgeToFirst = item("bridge-a", "bridge.example", SourceType.NEWS_REPORT, 0.8,
            primary = "wire-a", summary = "Pomost pierwszej relacji.")
        val bridgeToThird = item("bridge-b", "bridge.example", SourceType.NEWS_REPORT, 0.8,
            primary = "wire-b", summary = "Pomost trzeciej relacji.")
        val clusters = SourceIndependenceAnalyzer().cluster(
            listOf(first, third, bridgeToFirst, bridgeToThird)
        )
        assertEquals(1, clusters.size)
    }

    private fun engine(items: List<EvidenceItem>): EvidenceSet =
        EvidenceEngine(listOf(provider(items))).evaluate(claim, ARTICLE, "article.example")

    private fun provider(items: List<EvidenceItem>) = object : EvidenceProvider {
        override val id = "fake"
        override fun findEvidence(query: EvidenceQuery) = items.map { it.copy(claimId = query.claim.id) }
    }

    private fun item(
        suffix: String, domain: String, type: SourceType, confidence: Double,
        stance: EvidenceStance = EvidenceStance.SUPPORTS, primary: String? = null,
        summary: String = "Bezpośredni dokument źródłowy $suffix potwierdza analizowane twierdzenie."
    ) = EvidenceItem(claim.id, summary, "https://$domain/$suffix", domain, "Publisher $suffix",
        publicationDate = "2026-08-30", eventDate = "2026-08-30", sourceType = type,
        stance = stance, confidence = confidence, provenance = EvidenceProvenance.TEST_FAKE,
        primarySourceId = primary, direct = true)

    companion object { private const val ARTICLE = "https://article.example/story" }
}
