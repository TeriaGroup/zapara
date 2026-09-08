package ru.bgtu_voenmeh.zapara.data.api

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.io.InputStream
import java.net.HttpURLConnection
import java.net.URL
import kotlin.math.min

data class HttpCall(
    val method: String,
    val url: String,
    val headers: Map<String, String> = emptyMap(),
    val body: ByteArray? = null,
    val maxBytes: Int = 16 * 1024 * 1024
)

data class HttpReply(
    val status: Int,
    val body: ByteArray,
    val contentType: String? = "application/json",
    val headers: Map<String, String> = emptyMap()
)

fun interface HttpExchange {
    suspend fun exchange(call: HttpCall): HttpReply
}

class HttpBodyTooLargeException : IOException("body too large")

object HttpBodies {
    fun readLimited(stream: InputStream, limit: Int): ByteArray {
        val out = ByteArrayOutputStream()
        val buf = ByteArray(min(16384, limit + 1).coerceAtLeast(1))
        var total = 0
        while (true) {
            val n = stream.read(buf, 0, min(buf.size, limit - total + 1))
            if (n <= 0) break
            if (total + n > limit) throw HttpBodyTooLargeException()
            out.write(buf, 0, n)
            total += n
        }
        return out.toByteArray()
    }
}

class UrlConnectionTransport : HttpExchange {
    override suspend fun exchange(call: HttpCall): HttpReply = withContext(Dispatchers.IO) {
        val conn = URL(call.url).openConnection() as HttpURLConnection
        try {
            conn.instanceFollowRedirects = false
            conn.useCaches = false
            conn.connectTimeout = 20_000
            conn.readTimeout = 30_000
            conn.requestMethod = call.method
            conn.setRequestProperty("Accept", "application/json")
            conn.setRequestProperty("Accept-Encoding", "identity")
            for ((k, v) in call.headers) {
                conn.setRequestProperty(k, v)
            }
            val payload = call.body
            if (payload != null) {
                conn.doOutput = true
                conn.setRequestProperty("Content-Type", "application/json")
                conn.outputStream.use { it.write(payload) }
            }
            val status = conn.responseCode
            val headerMap = linkedMapOf<String, String>()
            conn.headerFields?.forEach { (name, values) ->
                if (name != null && !values.isNullOrEmpty()) headerMap[name] = values[0]
            }
            val stream = if (status in 200..299) conn.inputStream else conn.errorStream
            val bytes = if (stream == null) ByteArray(0) else HttpBodies.readLimited(stream, call.maxBytes)
            HttpReply(status, bytes, conn.contentType, headerMap)
        } finally {
            conn.disconnect()
        }
    }
}
