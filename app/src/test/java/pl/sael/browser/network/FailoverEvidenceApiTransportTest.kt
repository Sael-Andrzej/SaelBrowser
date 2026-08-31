package pl.sael.browser.network

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test
import java.net.UnknownHostException

class FailoverEvidenceApiTransportTest {
    @Test fun `primary response is used without calling fallback`() {
        var fallbackCalls = 0
        val transport = FailoverEvidenceApiTransport(listOf(
            EvidenceApiTransport { EvidenceApiResponse(200, "primary") },
            EvidenceApiTransport { fallbackCalls++; EvidenceApiResponse(200, "fallback") }
        ))

        assertEquals("primary", transport.postEvidence("{}").body)
        assertEquals(0, fallbackCalls)
    }

    @Test fun `dns failure uses fallback`() {
        val transport = FailoverEvidenceApiTransport(listOf(
            EvidenceApiTransport { throw UnknownHostException("primary") },
            EvidenceApiTransport { EvidenceApiResponse(200, "fallback") }
        ))

        assertEquals("fallback", transport.postEvidence("{}").body)
    }

    @Test fun `gateway failure uses fallback but client error does not`() {
        var fallbackCalls = 0
        val fallback = EvidenceApiTransport { fallbackCalls++; EvidenceApiResponse(200, "fallback") }
        val gateway = FailoverEvidenceApiTransport(listOf(
            EvidenceApiTransport { EvidenceApiResponse(502, "gateway") }, fallback
        ))
        val clientError = FailoverEvidenceApiTransport(listOf(
            EvidenceApiTransport { EvidenceApiResponse(400, "bad request") }, fallback
        ))

        assertEquals("fallback", gateway.postEvidence("{}").body)
        assertEquals(400, clientError.postEvidence("{}").statusCode)
        assertEquals(1, fallbackCalls)
    }

    @Test fun `last connection failure is propagated for safe empty evidence handling`() {
        val transport = FailoverEvidenceApiTransport(listOf(
            EvidenceApiTransport { throw UnknownHostException("primary") },
            EvidenceApiTransport { throw IllegalStateException("fallback") }
        ))

        assertThrows(IllegalStateException::class.java) { transport.postEvidence("{}") }
    }
}
