package ru.bgtu_voenmeh.zapara.data.communities

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.api.FakeHttp
import ru.bgtu_voenmeh.zapara.data.api.HttpBodyTooLargeException
import ru.bgtu_voenmeh.zapara.data.api.HttpReply
import java.io.IOException

class CommunityHttpClientFailureTest {
    @Test
    fun maps_401_403_404_409_and_related_codes() = runBlocking {
        val cases = listOf(
            Triple(401, "invalid_session", CommunityClientFailure.InvalidSession),
            Triple(403, "forbidden", CommunityClientFailure.Forbidden),
            Triple(404, "not_found", CommunityClientFailure.NotFound),
            Triple(409, "revision_conflict", CommunityClientFailure.RevisionConflict),
            Triple(409, "already_voted", CommunityClientFailure.AlreadyVoted),
            Triple(409, "already_member", CommunityClientFailure.AlreadyMember),
            Triple(409, "already_requested", CommunityClientFailure.AlreadyRequested),
            Triple(409, "poll_closed", CommunityClientFailure.PollClosed),
            Triple(400, "invalid_request", CommunityClientFailure.InvalidRequest),
            Triple(413, "payload_too_large", CommunityClientFailure.PayloadTooLarge),
            Triple(429, "rate_limited", CommunityClientFailure.RateLimited),
            Triple(503, "db_unavailable", CommunityClientFailure.DbUnavailable)
        )
        for ((status, code, failure) in cases) {
            val http = FakeHttp {
                HttpReply(status, """{"title":"Ошибка","status":$status,"code":"$code"}""".toByteArray())
            }
            try {
                client(http).list(ACCESS)
                fail(code)
            } catch (e: CommunityClientException) {
                assertEquals(code, failure, e.failure)
            }
        }
        val bare = FakeHttp { HttpReply(401, ByteArray(0)) }
        expect(CommunityClientFailure.InvalidSession) { client(bare).get(ACCESS, CID) }
        val conflict = FakeHttp { HttpReply(409, ByteArray(0)) }
        expect(CommunityClientFailure.Conflict) { client(conflict).requestJoin(ACCESS, CID) }
    }

    @Test
    fun rejects_content_encoding_extra_fields_and_oversized_read() = runBlocking {
        val encoded = FakeHttp {
            HttpReply(200, """[]""".toByteArray(), headers = mapOf("Content-Encoding" to "gzip"))
        }
        expect(CommunityClientFailure.InvalidPayload) { client(encoded).list(ACCESS) }
        val extra = FakeHttp {
            HttpReply(
                200,
                """[{"communityId":"$CID","name":"Группа О3313","description":"","revision":1,"role":"member","groupId":"O3313"}]""".toByteArray()
            )
        }
        expect(CommunityClientFailure.InvalidPayload) { client(extra).list(ACCESS) }
        val huge = FakeHttp { HttpReply(200, ByteArray(CommunityValidation.RequestBytes + 1) { 'x'.code.toByte() }) }
        expect(CommunityClientFailure.BodyTooLarge) { client(huge).list(ACCESS) }
        val limited = FakeHttp { throw HttpBodyTooLargeException() }
        expect(CommunityClientFailure.BodyTooLarge) { client(limited).list(ACCESS) }
        val down = FakeHttp { throw IOException("offline") }
        expect(CommunityClientFailure.Transport) { client(down).list(ACCESS) }
    }

    @Test
    fun write_cap_and_invalid_token_are_local() = runBlocking {
        val http = FakeHttp { HttpReply(200, """[]""".toByteArray()) }
        try {
            client(http).list("nope")
            fail()
        } catch (e: IllegalArgumentException) {
            assertEquals("Недопустимые данные аккаунта.", e.message)
        }
        assertTrue(http.requests.isEmpty())
        try {
            CommunityValidation.body("Я".repeat(8001))
            fail()
        } catch (e: IllegalArgumentException) {
            assertEquals("Недопустимый контракт сообщества.", e.message)
        }
    }

    private suspend fun expect(failure: CommunityClientFailure, block: suspend () -> Unit) {
        try {
            block()
            fail(failure.name)
        } catch (e: CommunityClientException) {
            assertEquals(failure, e.failure)
        }
    }
}

private val SCOPE = AccountServerScope.parse("https://example.invalid/root")
private val ACCESS: String = run {
    val encoded = java.util.Base64.getUrlEncoder().withoutPadding().encodeToString(ByteArray(32) { 1 })
    "za_$encoded"
}
private const val CID = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
private fun client(http: FakeHttp) = CommunityHttpClient(http, SCOPE)
