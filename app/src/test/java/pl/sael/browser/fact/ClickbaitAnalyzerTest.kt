package pl.sael.browser.fact

import org.junit.Assert.assertTrue
import org.junit.Test

class ClickbaitAnalyzerTest {
    private val analyzer = LocalClickbaitAnalyzer()

    @Test
    fun `detects sensational title signals`() {
        val result = analyzer.analyze(
            "PILNE!!! SZOK! MUSISZ TO ZOBACZYĆ!!!",
            "Spokojna treść opisująca wydarzenie i jego okoliczności. ".repeat(10)
        )
        assertTrue(result.score >= 0.6)
        assertTrue(result.reasons.isNotEmpty())
    }

    @Test
    fun `detects title content mismatch without making a verdict`() {
        val result = analyzer.analyze(
            "Naukowcy opisują niezwykłe odkrycie na powierzchni Marsa",
            "Raport gospodarczy omawia ceny energii, inflację, podatki i rynek pracy. ".repeat(8)
        )
        assertTrue(result.reasons.any { it.contains("pokrycie tematyczne") })
    }
}
