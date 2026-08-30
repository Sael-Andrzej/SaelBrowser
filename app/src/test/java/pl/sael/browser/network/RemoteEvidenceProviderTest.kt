package pl.sael.browser.network

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import pl.sael.browser.fact.claim.Claim
import pl.sael.browser.fact.claim.ClaimType
import pl.sael.browser.fact.evidence.EvidenceQuery
import pl.sael.browser.fact.evidence.EvidenceStance
import pl.sael.browser.fact.evidence.SourceType
import java.net.SocketTimeoutException
import java.util.concurrent.Executors

class RemoteEvidenceProviderTest {
    private val claim = Claim("claim-1", "Inflacja w Polsce wyniosła 3,1%.", ClaimType.FACTUAL,
        1.0, "Inflacja w Polsce wyniosła 3,1%.", claimDate = "2026-08-30")
    private val query = EvidenceQuery(claim,
        "https://article.example/private/path?token=secret", "article.example")

    @Test fun `backend offline and timeout safely return no evidence`() {
        listOf(RuntimeException("offline"), SocketTimeoutException("timeout")).forEach { error ->
            val provider = RemoteEvidenceProvider(EvidenceApiTransport { throw error })
            assertTrue(provider.findEvidence(query).isEmpty())
        }
    }

    @Test fun `http error and malformed json safely return no evidence`() {
        val error = RemoteEvidenceProvider(EvidenceApiTransport { EvidenceApiResponse(500, "{}") })
        val malformed = RemoteEvidenceProvider(EvidenceApiTransport { EvidenceApiResponse(200, "not-json") })
        assertTrue(error.findEvidence(query).isEmpty())
        assertTrue(malformed.findEvidence(query).isEmpty())
    }

    @Test fun `valid response maps to evidence item`() {
        val provider = RemoteEvidenceProvider(EvidenceApiTransport { EvidenceApiResponse(200, responseJson()) })
        val item = provider.findEvidence(query).single()
        assertEquals(claim.id, item.claimId)
        assertEquals("facts.example", item.domain)
        assertEquals(SourceType.FACT_CHECK, item.sourceType)
        assertEquals(EvidenceStance.UNKNOWN, item.stance)
    }

    @Test fun `declared fake domain is rejected`() {
        val fakeDomain = responseJson().replace("facts.example\"", "trusted.example\"")
        val provider = RemoteEvidenceProvider(EvidenceApiTransport { EvidenceApiResponse(200, fakeDomain) })
        assertTrue(provider.findEvidence(query).isEmpty())
    }

    @Test fun `private evidence url is rejected even when declared domain matches`() {
        listOf("127.0.0.1", "2130706433", "0x7f000001").forEach { host ->
            val privateUrl = responseJson()
                .replace("https://facts.example/review", "https://$host/review")
                .replace("facts.example\"", "$host\"")
            val provider = RemoteEvidenceProvider(EvidenceApiTransport { EvidenceApiResponse(200, privateUrl) })
            assertTrue(host, provider.findEvidence(query).isEmpty())
        }
    }

    @Test fun `manipulated provider metadata cannot create decisive evidence`() {
        val manipulated = responseJson()
            .replace("\"sourceType\":\"FACT_CHECK\"", "\"sourceType\":\"PRIMARY_DOCUMENT\"")
            .replace("\"stance\":\"UNKNOWN\"", "\"stance\":\"SUPPORTS\"")
            .replace("\"providerConfidence\":0.75", "\"providerConfidence\":1.0")
        val provider = RemoteEvidenceProvider(EvidenceApiTransport { EvidenceApiResponse(200, manipulated) })
        assertTrue(provider.findEvidence(query).isEmpty())
    }

    @Test fun `response for another claim is rejected`() {
        listOf(
            "Inne twierdzenie",
            "inflacja w polsce wyniosła 3,1%.",
            "Inflacja w Polsce wyniosła 3,2%."
        ).forEach { otherClaim ->
            val swapped = responseJson().replace(
                "\"query\":\"Inflacja w Polsce wyniosła 3,1%.\"",
                "\"query\":\"$otherClaim\""
            )
            val provider = RemoteEvidenceProvider(EvidenceApiTransport { EvidenceApiResponse(200, swapped) })
            assertTrue(otherClaim, provider.findEvidence(query).isEmpty())
        }
    }

    @Test fun `claim binding accepts only unicode and whitespace normalization equivalents`() {
        listOf(
            "Inflacja  w Polsce wyniosła 3,1%.",
            "Inflacja w Polsce wyniosła 3,1%.\u00a0",
            "Inflacja w Polsce wyniosła ３,１%."
        ).forEach { equivalent ->
            val normalized = responseJson().replace(
                "\"query\":\"Inflacja w Polsce wyniosła 3,1%.\"",
                "\"query\":\"$equivalent\""
            )
            val provider = RemoteEvidenceProvider(EvidenceApiTransport { EvidenceApiResponse(200, normalized) })
            assertEquals(1, provider.findEvidence(query).size)
        }
    }

    @Test fun `concurrent responses arriving out of order remain bound to their claims`() {
        val secondClaim = claim.copy(id = "claim-2", text = "Bezrobocie w Polsce wyniosło 4,0%.")
        val transport = EvidenceApiTransport { requestBody ->
            val requested = if (requestBody.contains("Bezrobocie")) secondClaim.text else claim.text
            if (requested == claim.text) Thread.sleep(30)
            EvidenceApiResponse(200, responseJson().replace(claim.text, requested))
        }
        val provider = RemoteEvidenceProvider(transport)
        val executor = Executors.newFixedThreadPool(2)
        try {
            val first = executor.submit<List<pl.sael.browser.fact.evidence.EvidenceItem>> {
                provider.findEvidence(query)
            }
            val second = executor.submit<List<pl.sael.browser.fact.evidence.EvidenceItem>> {
                provider.findEvidence(query.copy(claim = secondClaim))
            }
            assertEquals("claim-1", first.get().single().claimId)
            assertEquals("claim-2", second.get().single().claimId)
        } finally {
            executor.shutdownNow()
        }
    }

    @Test fun `request sends only claim language date and sanitized source origin`() {
        var captured = ""
        val provider = RemoteEvidenceProvider(EvidenceApiTransport { body ->
            captured = body
            EvidenceApiResponse(200, "{\"evidence\":[]}")
        })
        provider.findEvidence(query)
        assertTrue(captured.contains("Inflacja w Polsce"))
        assertTrue(captured.contains("https://article.example/"))
        assertFalse(captured.contains("private/path"))
        assertFalse(captured.contains("token=secret"))
        assertFalse(captured.contains("content"))
    }

    private fun responseJson() = """
        {"query":"Inflacja w Polsce wyniosła 3,1%.", "warnings":[], "evidence":[{
          "id":"one", "claim":"Inflacja", "snippet":"Niezależny opis dowodu",
          "url":"https://facts.example/review", "domain":"facts.example",
          "publisher":"Fact Checker", "author":null, "publishedAt":"2026-08-30",
          "eventDate":"2026-08-30", "sourceType":"FACT_CHECK", "stance":"UNKNOWN",
          "provenance":"GOOGLE_FACT_CHECK", "primarySourceId":"review-one",
          "provider":"google-fact-check", "providerConfidence":0.75
        }]}
    """.trimIndent()
}
