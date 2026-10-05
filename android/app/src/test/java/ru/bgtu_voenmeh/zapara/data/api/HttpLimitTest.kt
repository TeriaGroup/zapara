package ru.bgtu_voenmeh.zapara.data.api

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import java.io.InputStream
import java.net.URI

class HttpLimitTest {
    @Test
    fun readLimited_throws_before_consuming_unbounded_stream() {
        var reads = 0
        val stream = object : InputStream() {
            override fun read(): Int {
                reads++
                return 'x'.code
            }
        }
        try {
            HttpBodies.readLimited(stream, 32)
            fail()
        } catch (_: HttpBodyTooLargeException) {
        }
        assertTrue(reads < 10_000)
    }

    @Test
    fun content_encoding_is_invalid_payload() = runBlocking {
        val http = FakeHttp {
            HttpReply(200, catalogJson().toByteArray(), headers = mapOf("Content-Encoding" to "gzip"))
        }
        val client = TimetableApiClient(http, URI("https://example.invalid/"))
        try {
            client.fetch(emptyList())
            fail()
        } catch (e: TimetableApiException) {
            assertEquals(TimetableApiFailure.InvalidPayload, e.failure)
        }
    }

    @Test
    fun fetch_passes_body_cap_on_the_call() = runBlocking {
        val http = FakeHttp { jsonReply(catalogJson()) }
        val client = TimetableApiClient(http, URI("https://example.invalid/"))
        client.fetch(emptyList())
        assertEquals(16 * 1024 * 1024, http.requests.single().maxBytes)
    }
}
