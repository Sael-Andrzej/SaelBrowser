package pl.sael.browser.network

class FailoverEvidenceApiTransport(
    private val transports: List<EvidenceApiTransport>
) : EvidenceApiTransport {
    init {
        require(transports.isNotEmpty())
    }

    override fun postEvidence(jsonBody: String): EvidenceApiResponse {
        var lastFailure: Exception? = null
        var lastResponse: EvidenceApiResponse? = null
        transports.forEachIndexed { index, transport ->
            try {
                val response = transport.postEvidence(jsonBody)
                lastResponse = response
                if (response.statusCode !in RETRYABLE_GATEWAY_ERRORS || index == transports.lastIndex) {
                    return response
                }
            } catch (failure: Exception) {
                lastFailure = failure
                if (index == transports.lastIndex) throw failure
            }
        }
        return lastResponse ?: throw lastFailure ?: IllegalStateException("No evidence backend available")
    }

    private companion object {
        val RETRYABLE_GATEWAY_ERRORS = setOf(502, 503, 504)
    }
}
