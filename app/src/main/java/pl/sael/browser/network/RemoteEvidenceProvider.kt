package pl.sael.browser.network

import com.google.gson.JsonObject
import pl.sael.browser.fact.evidence.EvidenceItem
import pl.sael.browser.fact.evidence.EvidenceProvider
import pl.sael.browser.fact.evidence.EvidenceQuery
import java.net.URI

class RemoteEvidenceProvider(
    private val transport: EvidenceApiTransport?,
    private val mapper: EvidenceApiMapper = EvidenceApiMapper()
) : EvidenceProvider {
    override val id = "sael-evidence-backend"

    override fun findEvidence(query: EvidenceQuery): List<EvidenceItem> {
        val api = transport ?: return emptyList()
        return runCatching {
            val request = JsonObject().apply {
                addProperty("claim", query.claim.text.take(MAX_CLAIM_LENGTH))
                addProperty("language", "pl")
                query.claim.claimDate?.let { addProperty("publishedAt", it.take(10)) }
                publicOrigin(query.articleUrl)?.let { addProperty("sourceUrl", it) }
            }
            val response = api.postEvidence(request.toString())
            if (response.statusCode !in 200..299) emptyList()
            else mapper.map(response.body, query.claim.id, query.claim.text.take(MAX_CLAIM_LENGTH))
        }.getOrDefault(emptyList())
    }

    private fun publicOrigin(url: String): String? = runCatching {
        val uri = URI(url)
        if (uri.scheme != "https" || uri.host.isNullOrBlank() || uri.userInfo != null) null
        else URI("https", null, uri.host, if (uri.port == 443) -1 else uri.port, "/", null, null).toString()
    }.getOrNull()

    companion object {
        private const val MAX_CLAIM_LENGTH = 500

        fun configured(baseUrl: String): RemoteEvidenceProvider {
            val transport = baseUrl.takeIf(String::isNotBlank)?.let {
                runCatching {
                    HttpEvidenceApiTransport(it, allowDebugLoopbackHttp = pl.sael.browser.BuildConfig.DEBUG)
                }.getOrNull()
            }
            return RemoteEvidenceProvider(transport)
        }
    }
}
