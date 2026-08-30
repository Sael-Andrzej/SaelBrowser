package pl.sael.browser.network

data class EvidenceApiResponse(val statusCode: Int, val body: String)

fun interface EvidenceApiTransport {
    fun postEvidence(jsonBody: String): EvidenceApiResponse
}
