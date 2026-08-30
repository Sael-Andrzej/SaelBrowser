package pl.sael.browser.fact.providers

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import pl.sael.browser.fact.claim.Claim
import pl.sael.browser.fact.claim.ClaimType
import pl.sael.browser.fact.evidence.EvidenceQuery
import pl.sael.browser.fact.evidence.EvidenceStance
import pl.sael.browser.fact.evidence.SourceType

class ProviderAdaptersTest {
    private val query = EvidenceQuery(
        Claim("id", "Firma ogłosiła upadłość 2026-08-30.", ClaimType.FACTUAL, 1.0, "kontekst"),
        "https://article.example", "article.example"
    )

    @Test fun `unconfigured production adapters safely return no evidence`() {
        assertTrue(FactCheckApiProvider().findEvidence(query).isEmpty())
        assertTrue(WebSearchProvider().findEvidence(query).isEmpty())
    }

    @Test fun `fact check adapter normalizes documented client result`() {
        val provider = FactCheckApiProvider(object : FactCheckApiClient {
            override fun search(query: String) = listOf(FactCheckRecord(
                query, "Zweryfikowano w niezależnej bazie.", "https://facts.example/check",
                "Fact Checker", "2026-08-30", false, 0.9
            ))
        })
        val item = provider.findEvidence(this.query).single()
        assertEquals(EvidenceStance.REFUTES, item.stance)
        assertEquals(SourceType.FACT_CHECK, item.sourceType)
    }
}
