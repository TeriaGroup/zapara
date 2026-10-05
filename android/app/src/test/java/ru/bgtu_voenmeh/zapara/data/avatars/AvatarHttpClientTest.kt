package ru.bgtu_voenmeh.zapara.data.avatars

import kotlinx.coroutines.*
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.api.*

class AvatarHttpClientTest {
    private val user = "11111111-1111-4111-8111-111111111111"
    private val peer = "22222222-2222-4222-8222-222222222222"
    private val token = "za_" + "A".repeat(43)
    private val scope = AccountServerScope.parse("https://example.test")
    private val target get() = AvatarTarget(AvatarKind.User, user)
    private fun webp(marker: Int = 1) = "RIFFxxxxWEBP".toByteArray() + byteArrayOf(marker.toByte())

    @Test fun photo_uses_bearer_bounded_body_and_conditional_get() = runBlocking {
        val http = FakeHttp { HttpReply(200, webp(), "image/webp", mapOf("ETag" to "\"revision\"")) }
        val result = AvatarHttpClient(http, scope).download(token, target, "\"old\"")
        assertArrayEquals(webp(), result.bytes)
        val request = http.requests.single()
        assertEquals("https://example.test/api/v1/social/avatars/users/$user", request.url)
        assertEquals("Bearer $token", request.headers["Authorization"])
        assertEquals("\"old\"", request.headers["If-None-Match"])
        assertEquals(512 * 1024, request.maxBytes)
        assertFalse(request.url.contains(token))
    }

    @Test fun absent_avatar_and_unchanged_revision_are_distinct() = runBlocking {
        assertNull(AvatarHttpClient(FakeHttp { HttpReply(404, byteArrayOf()) }, scope).download(token, target).bytes)
        val cached = AvatarHttpClient(FakeHttp { HttpReply(304, byteArrayOf()) }, scope).download(token, target, "\"a\"")
        assertTrue(cached.unchanged)
        try { AvatarHttpClient(FakeHttp { HttpReply(304, byteArrayOf()) }, scope).download(token, target); fail() }
        catch (error: AvatarFailure) { assertEquals(304, error.status) }
    }

    @Test fun rejects_wrong_format_oversize_and_untrusted_paths() = runBlocking {
        for (reply in listOf(HttpReply(200, webp(), "text/html"), HttpReply(200, ByteArray(512 * 1024 + 1), "image/webp"))) {
            try { AvatarHttpClient(FakeHttp { reply }, scope).download(token, target); fail() }
            catch (error: AvatarFailure) { assertEquals(502, error.status) }
        }
        val http = FakeHttp { error("Invalid target must not issue a request") }
        try { AvatarHttpClient(http, scope).download(token, AvatarTarget(AvatarKind.User, "../me")); fail() }
        catch (_: IllegalArgumentException) { }
        assertTrue(http.requests.isEmpty())
    }

    @Test fun upload_has_one_file_field_and_separate_self_and_group_routes() = runBlocking {
        val http = FakeHttp { HttpReply(200, """{"revision":"$peer"}""".toByteArray()) }
        val client = AvatarHttpClient(http, scope)
        assertEquals(peer, client.upload(token, target, webp()))
        assertTrue(http.requests.last().url.endsWith("/avatars/me"))
        assertEquals(1, Regex("name=\"file\"").findAll(http.requests.last().body!!.toString(Charsets.UTF_8)).count())
        client.upload(token, AvatarTarget(AvatarKind.Group, peer), webp())
        assertTrue(http.requests.last().url.endsWith("/avatars/groups/$peer"))
        try { client.upload(token, target, ByteArray(3 * 1024 * 1024 + 1)); fail() } catch (_: IllegalArgumentException) { }
        assertEquals(2, http.requests.size)
    }

    @Test fun revoked_session_drops_cached_photo_and_closed_profile_does_not_fetch() = runBlocking {
        var at = 0L
        var deny = false
        val http = FakeHttp { if (deny) HttpReply(401, byteArrayOf()) else HttpReply(200, webp(), "image/webp") }
        val store = AvatarStore(user, AvatarHttpClient(http, scope), { token }, clock = { at })
        assertNotNull(store.load(target))
        at = 60_000; deny = true
        assertNull(store.load(target))
        val count = http.requests.size
        store.close()
        assertNull(store.load(target))
        assertEquals(count, http.requests.size)
    }

    @Test fun missing_session_does_not_trigger_an_infinite_revision_loop() = runBlocking {
        val store = AvatarStore(user, AvatarHttpClient(FakeHttp { error("Guest must not fetch") }, scope), { null })
        repeat(3) { assertNull(store.load(target)) }
        assertEquals(0L, store.revision.value)
    }

    @Test fun cannot_edit_another_users_photo() = runBlocking {
        val http = FakeHttp { error("Wrong owner must not fetch") }
        val store = AvatarStore(user, AvatarHttpClient(http, scope), { token })
        try { store.upload(AvatarTarget(AvatarKind.User, peer), webp()); fail() }
        catch (error: AvatarFailure) { assertEquals(403, error.status) }
        assertTrue(http.requests.isEmpty())
    }

    @Test fun old_download_cannot_overwrite_a_successful_photo_change() = runBlocking {
        val entered = CompletableDeferred<Unit>()
        val finish = CompletableDeferred<Unit>()
        var first = true
        val client = AvatarHttpClient(HttpExchange { call ->
            if (call.method == "PUT") HttpReply(200, """{"revision":"$peer"}""".toByteArray())
            else if (first) { first = false; entered.complete(Unit); finish.await(); HttpReply(200, webp(1), "image/webp") }
            else HttpReply(200, webp(2), "image/webp")
        }, scope)
        val store = AvatarStore(user, client, { token })
        val old = async { store.load(target) }
        entered.await()
        store.upload(target, webp(2))
        finish.complete(Unit)
        assertNull(old.await())
        assertArrayEquals(webp(2), store.load(target))
    }

    @Test fun queued_edit_does_not_start_after_account_profile_is_closed() = runBlocking {
        val entered = CompletableDeferred<Unit>()
        val finish = CompletableDeferred<Unit>()
        val http = FakeHttp { error("Closed profile must not send an edit") }
        val store = AvatarStore(user, AvatarHttpClient(http, scope), {
            entered.complete(Unit); finish.await(); token
        })
        val edit = async {
            try { store.upload(target, webp()); false }
            catch (error: AvatarFailure) { error.status == 403 }
        }
        entered.await()
        store.close()
        finish.complete(Unit)
        assertTrue(edit.await())
        assertTrue(http.requests.isEmpty())
    }

    @Test fun closed_profile_discards_late_photo_reply() = runBlocking {
        val entered = CompletableDeferred<Unit>()
        val finish = CompletableDeferred<Unit>()
        val store = AvatarStore(user, AvatarHttpClient(HttpExchange {
            entered.complete(Unit); finish.await(); HttpReply(200, webp(), "image/webp")
        }, scope), { token })
        val load = async { store.load(target) }
        entered.await()
        store.close()
        finish.complete(Unit)
        assertNull(load.await())
        assertNull(store.load(target))
    }
}
