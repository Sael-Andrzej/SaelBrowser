package pl.sael.browser.network

import java.io.ByteArrayOutputStream
import java.net.HttpURLConnection
import java.net.URI
import java.net.URL

class HttpEvidenceApiTransport(
    baseUrl: String,
    private val connectTimeoutMillis: Int = 3_000,
    private val readTimeoutMillis: Int = 5_000,
    allowDebugLoopbackHttp: Boolean = false
) : EvidenceApiTransport {
    private val endpoint: URL

    init {
        val base = URI(baseUrl.trim().trimEnd('/'))
        val loopbackDebug = allowDebugLoopbackHttp && base.scheme == "http" &&
            base.host in setOf("127.0.0.1", "localhost", "10.0.2.2")
        require(base.scheme == "https" || loopbackDebug) { "Evidence backend requires HTTPS" }
        require(base.userInfo == null && base.host != null && base.query == null && base.fragment == null)
        endpoint = base.resolve(base.path.trimEnd('/') + "/api/v1/evidence").toURL()
    }

    override fun postEvidence(jsonBody: String): EvidenceApiResponse {
        require(jsonBody.toByteArray().size <= MAX_REQUEST_BYTES)
        val connection = endpoint.openConnection() as HttpURLConnection
        try {
            connection.requestMethod = "POST"
            connection.connectTimeout = connectTimeoutMillis
            connection.readTimeout = readTimeoutMillis
            connection.instanceFollowRedirects = false
            connection.doOutput = true
            connection.setRequestProperty("Content-Type", "application/json; charset=utf-8")
            connection.setRequestProperty("Accept", "application/json")
            connection.outputStream.use { it.write(jsonBody.toByteArray(Charsets.UTF_8)) }
            val status = connection.responseCode
            val stream = if (status in 200..299) connection.inputStream else connection.errorStream
            val body = stream?.use(::readLimited).orEmpty()
            return EvidenceApiResponse(status, body)
        } finally {
            connection.disconnect()
        }
    }

    private fun readLimited(stream: java.io.InputStream): String {
        val output = ByteArrayOutputStream()
        val buffer = ByteArray(8_192)
        var total = 0
        while (true) {
            val read = stream.read(buffer)
            if (read < 0) break
            total += read
            require(total <= MAX_RESPONSE_BYTES) { "Evidence response too large" }
            output.write(buffer, 0, read)
        }
        return output.toString(Charsets.UTF_8.name())
    }

    companion object {
        private const val MAX_REQUEST_BYTES = 4_096
        private const val MAX_RESPONSE_BYTES = 256 * 1_024
    }
}
