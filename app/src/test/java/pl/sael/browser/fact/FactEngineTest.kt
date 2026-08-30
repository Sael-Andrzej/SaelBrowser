package pl.sael.browser.fact

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class FactEngineTest {
    private val article = ArticleInput(
        title = "Jednoznaczne twierdzenie testowe",
        content = "To jest wystarczająco długa treść artykułu testowego. ".repeat(5),
        url = "https://example.com/article",
        domain = "example.com"
    )

    @Test
    fun `returns unknown when evidence is missing`() {
        val result = ThresholdFactEngine(providers = listOf(LocalEvidenceProvider())).evaluate(
            ArticleInput("", "", "https://example.com", "example.com")
        )
        assertEquals(FactVerdict.UNKNOWN, result.verdict)
        assertTrue(result.rationale.contains("Brak niezależnego"))
    }

    @Test
    fun `returns true only for independent decisive evidence above threshold`() {
        val result = ThresholdFactEngine(
            providers = listOf(provider(EvidenceStance.SUPPORTS_TRUE, 0.92))
        ).evaluate(article)
        assertEquals(FactVerdict.TRUE, result.verdict)
        assertEquals(0.92, result.confidence, 0.0001)
    }

    @Test
    fun `returns false only for independent decisive contradiction above threshold`() {
        val result = ThresholdFactEngine(
            providers = listOf(provider(EvidenceStance.SUPPORTS_FALSE, 0.94))
        ).evaluate(article)
        assertEquals(FactVerdict.FALSE, result.verdict)
        assertEquals(0.94, result.confidence, 0.0001)
    }

    @Test
    fun `applies confidence threshold`() {
        val result = ThresholdFactEngine(
            providers = listOf(provider(EvidenceStance.SUPPORTS_TRUE, 0.79)),
            verdictThreshold = 0.8
        ).evaluate(article)
        assertEquals(FactVerdict.UNKNOWN, result.verdict)
        assertEquals(0.79, result.confidence, 0.0001)
    }

    @Test
    fun `conflicting decisive evidence returns unknown`() {
        val result = ThresholdFactEngine(
            providers = listOf(
                provider(EvidenceStance.SUPPORTS_TRUE, 0.95),
                provider(EvidenceStance.SUPPORTS_FALSE, 0.95)
            )
        ).evaluate(article)
        assertEquals(FactVerdict.UNKNOWN, result.verdict)
        assertEquals(0.0, result.confidence, 0.0001)
    }

    @Test
    fun `clickbait is not treated as false`() {
        val sensational = article.copy(
            title = "PILNE!!! SZOK! MUSISZ TO ZOBACZYĆ!!!"
        )
        val result = ThresholdFactEngine(providers = listOf(LocalEvidenceProvider())).evaluate(sensational)
        assertTrue(result.clickbait.score > 0.5)
        assertEquals(FactVerdict.UNKNOWN, result.verdict)
    }

    @Test
    fun `internal numeric contradiction remains unknown`() {
        val contradictory = article.copy(
            content = "Liczba mieszkańców miasta wynosi 1000. " +
                "Według dalszej części liczba mieszkańców miasta wynosi 2000. " +
                "Opis metodologii i kontekstu badania. ".repeat(5)
        )
        val result = ThresholdFactEngine(providers = listOf(LocalEvidenceProvider())).evaluate(contradictory)
        assertEquals(FactVerdict.UNKNOWN, result.verdict)
        assertTrue(result.evidence.none { it.stance == EvidenceStance.SUPPORTS_FALSE })
    }

    @Test
    fun `page supplied claim review cannot produce true or false even with trusted looking url`() {
        val result = ThresholdFactEngine().evaluate(
            article.copy(
                structuredFactChecks = listOf(
                    StructuredFactCheck(
                        "Twierdzenie deklarowane przez analizowaną stronę",
                        1.0,
                        "Znana nazwa redakcji",
                        "https://demagog.org.pl/podszyty-adres"
                    )
                )
            )
        )
        assertEquals(FactVerdict.UNKNOWN, result.verdict)
        assertTrue(result.evidence.none(FactEvidence::decisive))
    }

    @Test
    fun `decisive flag from page content is never enough for verdict`() {
        val pageProvider = object : FactEvidenceProvider {
            override val origin = ResultOrigin.LOCAL_HEURISTIC
            override fun collect(article: ArticleInput, clickbait: ClickbaitResult) = listOf(
                FactEvidence(
                    "Dane pochodzą wyłącznie ze strony",
                    EvidenceStance.SUPPORTS_TRUE,
                    1.0,
                    decisive = true,
                    provenance = EvidenceProvenance.PAGE_CONTENT
                )
            )
        }
        assertEquals(
            FactVerdict.UNKNOWN,
            ThresholdFactEngine(providers = listOf(pageProvider)).evaluate(article).verdict
        )
    }

    @Test
    fun `multiple evidence items do not inflate confidence`() {
        val duplicateProvider = object : FactEvidenceProvider {
            override val origin = ResultOrigin.EXTERNAL_SOURCE
            override fun collect(article: ArticleInput, clickbait: ClickbaitResult) = listOf(
                independentEvidence(EvidenceStance.SUPPORTS_TRUE, 0.82),
                independentEvidence(EvidenceStance.SUPPORTS_TRUE, 0.81),
                independentEvidence(EvidenceStance.SUPPORTS_TRUE, 0.80)
            )
        }
        val result = ThresholdFactEngine(providers = listOf(duplicateProvider)).evaluate(article)
        assertEquals(FactVerdict.TRUE, result.verdict)
        assertEquals(0.82, result.confidence, 0.0001)
    }

    @Test
    fun `resolved claim cannot make whole article true while another factual claim is unresolved`() {
        val first = pl.sael.browser.fact.claim.Claim(
            "first", "Inflacja wynosi 3,1%.", pl.sael.browser.fact.claim.ClaimType.FACTUAL,
            1.0, "Inflacja wynosi 3,1%."
        )
        val second = pl.sael.browser.fact.claim.Claim(
            "second", "Bezrobocie wynosi 4,0%.", pl.sael.browser.fact.claim.ClaimType.FACTUAL,
            0.9, "Bezrobocie wynosi 4,0%."
        )
        val evidenceProvider = object : pl.sael.browser.fact.evidence.EvidenceProvider {
            override val id = "independent-test"
            override fun findEvidence(query: pl.sael.browser.fact.evidence.EvidenceQuery): List<pl.sael.browser.fact.evidence.EvidenceItem> {
                if (query.claim.id != first.id) return emptyList()
                return listOf("official.example", "academic.example").mapIndexed { index, domain ->
                    pl.sael.browser.fact.evidence.EvidenceItem(
                        first.id, "Niezależny dokument ${index + 1} potwierdza wartość.",
                        "https://$domain/result", domain, "Publisher $index",
                        publicationDate = "2026-08-30", eventDate = "2026-08-30",
                        sourceType = if (index == 0) pl.sael.browser.fact.evidence.SourceType.PRIMARY_OFFICIAL
                            else pl.sael.browser.fact.evidence.SourceType.ACADEMIC,
                        stance = pl.sael.browser.fact.evidence.EvidenceStance.SUPPORTS,
                        confidence = 0.99,
                        provenance = pl.sael.browser.fact.evidence.EvidenceProvenance.TEST_FAKE,
                        direct = true
                    )
                }
            }
        }
        val result = ThresholdFactEngine(
            providers = emptyList(),
            claimExtractor = object : pl.sael.browser.fact.claim.ClaimExtractor {
                override fun extract(title: String, content: String, articleDate: String?) = listOf(first, second)
            },
            evidenceEngine = pl.sael.browser.fact.evidence.EvidenceEngine(listOf(evidenceProvider))
        ).evaluate(article)
        assertEquals(FactVerdict.UNKNOWN, result.verdict)
    }

    private fun provider(stance: EvidenceStance, strength: Double) = object : FactEvidenceProvider {
        override val origin = ResultOrigin.EXTERNAL_SOURCE
        override fun collect(article: ArticleInput, clickbait: ClickbaitResult) = listOf(
            independentEvidence(stance, strength)
        )
    }

    private fun independentEvidence(stance: EvidenceStance, strength: Double) = FactEvidence(
        "Niezależna przesłanka testowa",
        stance,
        strength,
        decisive = true,
        provenance = EvidenceProvenance.INDEPENDENT_SOURCE,
        source = FactSource("Niezależne źródło", "https://independent.example/evidence")
    )
}
