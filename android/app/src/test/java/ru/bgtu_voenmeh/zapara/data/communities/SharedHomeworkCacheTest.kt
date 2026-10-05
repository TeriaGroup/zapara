package ru.bgtu_voenmeh.zapara.data.communities
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.async
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineStart
import java.time.Instant
import org.junit.Test
import org.junit.Assert.*
import ru.bgtu_voenmeh.zapara.data.accounts.*
import ru.bgtu_voenmeh.zapara.data.api.*

class SharedHomeworkCacheTest {
    private val cid = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
    private val hid = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
    private val at = "2026-09-26T12:00:00Z"
    @Test fun known_membership_loss_or_access_error_never_preserves_shared_last_good() = runBlocking {
        for (failure in listOf(0, 401, 403, 404)) {
            var revoked = false
            val http = FakeHttp { call -> when {
                call.url.contains("/communities?") -> if (revoked && failure == 0) jsonReply("[]") else jsonReply("""[{"communityId":"$cid","name":"Group","description":"","revision":1,"role":"member"}]""")
                revoked -> HttpReply(failure, "{}".toByteArray())
                call.url.endsWith("/completion") -> jsonReply("""{"homeworkId":"$hid","completed":false,"revision":1,"updatedAt":"$at"}""")
                else -> jsonReply("""[{"homeworkId":"$hid","communityId":"$cid","title":"Math","body":"Private task","revision":1,"createdAt":"$at","updatedAt":"$at"}]""")
            } }
            val api = CommunityHttpClient(http, AccountServerScope.parse("https://example.invalid/"))
            val cache = SharedHomeworkCache(); val token = testToken("za_", 5)
            assertEquals("Private task", cache.refresh(api, token, "3313").snapshot!!.rows.single().body)
            revoked = true
            val result = cache.refresh(api, token, "3313")
            assertTrue("status $failure did not revoke", result.revoked); assertNull(result.snapshot)
        }
    }
    @Test fun network_failure_keeps_publicly_unconfirmed_shared_last_good() = runBlocking {
        var offline = false
        val http = FakeHttp { call ->
            if (offline) throw java.io.IOException("offline")
            if (call.url.contains("/communities?")) jsonReply("""[{"communityId":"$cid","name":"Group","description":"","revision":1,"role":"member"}]""")
            else jsonReply("[]")
        }
        val api = CommunityHttpClient(http, AccountServerScope.parse("https://example.invalid/")); val cache = SharedHomeworkCache(); val token = testToken("za_", 5)
        val before = cache.refresh(api, token, "3313").snapshot
        offline = true
        val after = cache.refresh(api, token, "3313")
        assertFalse(after.revoked); assertEquals(before, after.snapshot)
        assertTrue(after.failed)
    }
    @Test fun token_change_never_exposes_previous_owner_shared_cache_on_failure() = runBlocking {
        var offline = false
        val http = FakeHttp { call ->
            if (offline) throw java.io.IOException("offline")
            if (call.url.contains("/communities?")) jsonReply("""[{"communityId":"$cid","name":"Group","description":"","revision":1,"role":"member"}]""")
            else jsonReply("[]")
        }
        val api = CommunityHttpClient(http, AccountServerScope.parse("https://example.invalid/"))
        val cache = SharedHomeworkCache()
        assertNotNull(cache.refresh(api, testToken("za_", 5), "3313").snapshot)
        offline = true
        val changed = cache.refresh(api, testToken("za_", 6), "3313")
        assertNull(changed.snapshot)
        assertTrue(changed.failed)
    }
    @Test fun acknowledged_completion_survives_later_failed_refresh_with_new_revision() = runBlocking {
        var offline = false
        val http = FakeHttp { call ->
            if (offline) throw java.io.IOException("offline")
            when {
                call.url.contains("/communities?") -> jsonReply("""[{"communityId":"$cid","name":"Group","description":"","revision":1,"role":"member"}]""")
                call.url.endsWith("/completion") -> jsonReply("""{"homeworkId":"$hid","completed":false,"revision":1,"updatedAt":"$at"}""")
                else -> jsonReply("""[{"homeworkId":"$hid","communityId":"$cid","title":"Math","body":"Task","revision":1,"createdAt":"$at","updatedAt":"$at"}]""")
            }
        }
        val api = CommunityHttpClient(http, AccountServerScope.parse("https://example.invalid/"))
        val token = testToken("za_", 5)
        val cache = SharedHomeworkCache()
        assertFalse(cache.refresh(api, token, "3313").snapshot!!.completions.getValue(hid).completed)
        assertTrue(cache.acknowledgeCompletion("3313", cid, token,
            HomeworkCompletion(hid, true, 2, Instant.parse(at))))
        offline = true
        val after = cache.refresh(api, token, "3313")
        assertTrue(after.failed)
        assertEquals(2, after.snapshot!!.completions.getValue(hid).revision)
        assertTrue(after.snapshot!!.completions.getValue(hid).completed)
    }
    @Test fun later_network_failure_cannot_hide_an_already_requested_same_scope_denial() = runBlocking {
        val gate=CompletableDeferred<Unit>(); var phase=0
        val http=FakeHttp { call -> when(phase) {
            1 -> { gate.await(); HttpReply(403,"{}".toByteArray()) }
            2 -> throw java.io.IOException("offline")
            else -> if(call.url.contains("/communities?")) jsonReply("""[{"communityId":"$cid","name":"Group","description":"","revision":1,"role":"member"}]""") else jsonReply("[]")
        } }
        val api=CommunityHttpClient(http,AccountServerScope.parse("https://example.invalid/")); val cache=SharedHomeworkCache(); val token=testToken("za_",5)
        assertNotNull(cache.refresh(api,token,"3313").snapshot)
        phase=1; val denied=async(start=CoroutineStart.UNDISPATCHED) { cache.refresh(api,token,"3313") }
        phase=2; assertNotNull(cache.refresh(api,token,"3313").snapshot)
        gate.complete(Unit); val result=denied.await()
        assertTrue(result.revoked); assertNull(cache.snapshot)
    }

}
