package ru.bgtu_voenmeh.zapara.data.communities

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.fail
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.accounts.testToken
import ru.bgtu_voenmeh.zapara.data.api.FakeHttp
import ru.bgtu_voenmeh.zapara.data.api.HttpReply

class CommunityMediaDownloadTest {
    private val conversation = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
    private val message = "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeee1"
    private val token = testToken("za_", 4)

    @Test
    fun downloadsPrivateMediaThroughTheAuthenticatedConversationRoute() = runBlocking {
        val content = byteArrayOf(1, 2, 3)
        val http = FakeHttp { call ->
            assertEquals("GET", call.method)
            assertEquals(
                "http://127.0.0.1:9/api/v2/communities/conversations/$conversation/messages/$message/media",
                call.url
            )
            assertEquals("Bearer $token", call.headers["Authorization"])
            assertEquals("application/octet-stream", call.headers["Accept"])
            assertEquals(8 * 1024 * 1024, call.maxBytes)
            assertEquals(null, call.body)
            HttpReply(200, content, "application/octet-stream")
        }
        assertArrayEquals(content, client(http).downloadMedia(token, conversation, message))
        assertEquals(1, http.requests.size)
    }

    @Test
    fun deniesOversizedEmptyAndWrongContentTypeResponses() = runBlocking {
        for ((reply, expected) in listOf(
            HttpReply(200, ByteArray(8 * 1024 * 1024 + 1), "application/octet-stream") to CommunityClientFailure.PayloadTooLarge,
            HttpReply(200, ByteArray(0), "application/octet-stream") to CommunityClientFailure.InvalidPayload,
            HttpReply(200, byteArrayOf(1), "text/html") to CommunityClientFailure.InvalidPayload,
            HttpReply(200, byteArrayOf(1), "application/octet-stream", mapOf("Content-Encoding" to "gzip")) to CommunityClientFailure.InvalidPayload
        )) {
            val http = FakeHttp { reply }
            try {
                client(http).downloadMedia(token, conversation, message)
                fail("Expected $expected")
            } catch (e: CommunityClientException) {
                assertEquals(expected, e.failure)
            }
            assertEquals(1, http.requests.size)
        }
    }

    @Test
    fun preservesServerMembershipFailureAndValidatesIdsBeforeNetwork() = runBlocking {
        val http = FakeHttp { HttpReply(403, """{"title":"Нет доступа","status":403,"code":"forbidden"}""".toByteArray()) }
        try {
            client(http).downloadMedia(token, conversation, message)
            fail("Expected forbidden")
        } catch (e: CommunityClientException) {
            assertEquals(CommunityClientFailure.Forbidden, e.failure)
        }
        assertEquals(1, http.requests.size)
        try {
            client(http).downloadMedia(token, "../other", message)
            fail("Expected invalid ID")
        } catch (_: IllegalArgumentException) { }
        assertEquals(1, http.requests.size)
    }

    private fun client(http: FakeHttp) = CommunityHttpClient(http, AccountServerScope.parse("http://127.0.0.1:9/"))
}
