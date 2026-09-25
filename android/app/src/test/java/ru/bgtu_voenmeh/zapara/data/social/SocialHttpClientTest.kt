package ru.bgtu_voenmeh.zapara.data.social

import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.api.FakeHttp
import ru.bgtu_voenmeh.zapara.data.api.HttpReply

class SocialHttpClientTest {
    @Test fun home_uses_authenticated_native_route() = runBlocking {
        val http = FakeHttp { HttpReply(200, """{"code":"ABC123","friends":[],"incoming":[],"outgoing":[]}""".toByteArray()) }
        val api = SocialHttpClient(http, AccountServerScope.parse("https://example.test"))
        assertEquals("ABC123", api.home("za_" + "A".repeat(43)).code)
        assertEquals("https://example.test/api/v1/social/home", http.requests.single().url)
        assertEquals("Bearer za_" + "A".repeat(43), http.requests.single().headers["Authorization"])
    }
    @Test fun invalid_invite_does_not_send_request() = runBlocking {
        val http = FakeHttp { error("Must not call transport") }
        val api = SocialHttpClient(http, AccountServerScope.parse("https://example.test"))
        try { api.invite("za_" + "A".repeat(43), ""); fail() } catch (_: IllegalArgumentException) { }
        assertTrue(http.requests.isEmpty())
    }
    @Test fun server_error_is_not_parsed_as_home() = runBlocking {
        val api = SocialHttpClient(FakeHttp { HttpReply(503, "{}".toByteArray()) }, AccountServerScope.parse("https://example.test"))
        try { api.home("za_" + "A".repeat(43)); fail() } catch (e: SocialFailure) { assertEquals(503, e.status) }
    }
    @Test fun messages_and_mutations_preserve_reply_and_native_paths() = runBlocking {
        val target = "11111111-1111-4111-8111-111111111111"
        val conversation = "22222222-2222-4222-8222-222222222222"
        val message = """{"messageId":"$target","senderId":"$target","senderName":"Друг","body":"Привет","kind":"text","createdAt":"2026-01-01T00:00:00Z","replyTo":"$target","replyBody":"Вопрос","deleted":false,"editedAt":null,"read":true,"reactions":[{"emoji":"like","count":1,"mine":true}],"attachmentId":null,"fileName":null}"""
        val http = FakeHttp { call -> HttpReply(if (call.url.endsWith("/messages") && call.method == "POST") 201 else 200, (if (call.method == "GET") """{"messages":[$message],"hasMore":false}""" else message).toByteArray()) }
        val api = SocialHttpClient(http, AccountServerScope.parse("https://example.test"))
        val token = "za_" + "A".repeat(43)
        assertEquals(target, api.send(token, conversation, "Строка\n\"текст\"", target).replyTo)
        assertTrue(http.requests.last().body!!.toString(Charsets.UTF_8).contains("Строка\\n\\\"текст\\\""))
        assertTrue(api.messages(token, conversation, target).messages.single().reactions.single().mine)
        assertTrue(http.requests.last().url.endsWith("?before=$target"))
        api.edit(token, conversation, target, "Правка")
        assertTrue(http.requests.last().url.endsWith("/edit"))
        api.react(token, conversation, target, "heart")
        assertTrue(http.requests.last().url.endsWith("/reaction"))
        api.delete(token, conversation, target)
        assertTrue(http.requests.last().url.endsWith("/delete"))
    }
    @Test fun catch_up_fetches_until_it_reaches_known_history() = runBlocking {
        fun msg(id: String) = SocialMessage(id, "sender", "Имя", id, "text", java.time.Instant.EPOCH, null, null, false, false, false, emptyList(), null, null)
        val requested = mutableListOf<String?>()
        val updates = loadSocialUpdates(setOf("old")) { before ->
            requested += before
            when(before) {
                null -> SocialPage(listOf(msg("new3"), msg("new4")), true)
                "new3" -> SocialPage(listOf(msg("new1"), msg("new2")), true)
                "new1" -> SocialPage(listOf(msg("old")), false)
                else -> error("Unexpected page")
            }
        }
        assertEquals(listOf(null, "new3", "new1"), requested)
        assertEquals(listOf("old", "new1", "new2", "new3", "new4"), updates.messages.map { it.id })
    }
    @Test fun group_directs_keep_exact_conversation_and_community_scope() {
        val time = java.time.Instant.parse("2026-01-01T00:00:00Z")
        fun conversation(id: String, kind: String, at: java.time.Instant) = ru.bgtu_voenmeh.zapara.data.communities.Conversation(id, kind, "community", id, if (kind == "direct") "peer" else null, "Последнее", at, 0)
        val home = ru.bgtu_voenmeh.zapara.data.communities.GroupHome("community", "ИВТ-1", "ИВТ-1", conversation("group-chat", "group", time), emptyList(), listOf(conversation("direct-chat", "direct", time.plusSeconds(60))))
        val rows = orderInbox(groupInboxRows(home))
        assertEquals(listOf("direct-chat", "group-chat"), rows.map { it.id })
        assertTrue(rows.all { it.communityId == "community" })
        assertEquals(InboxSource.GroupDirect, rows.first().source)
        assertEquals(InboxSource.Group, rows.last().source)
        assertEquals("Личный чат · ИВТ-1", rows.first().subtitle)
        assertEquals("Учебная группа", rows.last().subtitle)
    }
    @Test fun inbox_orders_latest_then_unread_and_keeps_empty_chats() {
        val rows = listOf(InboxRow("a", "А", lastAt = java.time.Instant.parse("2026-01-01T00:00:00Z")), InboxRow("b", "Б", unread = 1), InboxRow("c", "В"))
        assertEquals(listOf("a", "b", "c"), orderInbox(rows).map { it.id })
    }
    @Test fun recorded_media_uses_authenticated_native_routes_and_duration() = runBlocking {
        val conversation = "22222222-2222-4222-8222-222222222222"
        val attachment = "33333333-3333-4333-8333-333333333333"
        val message = """{"messageId":"11111111-1111-4111-8111-111111111111","senderId":"11111111-1111-4111-8111-111111111111","senderName":"Друг","body":null,"kind":"voice","createdAt":"2026-01-01T00:00:00Z","replyTo":null,"replyBody":null,"deleted":false,"editedAt":null,"read":false,"reactions":[],"attachmentId":"$attachment","fileName":"voice.m4a","durationMs":1234}"""
        val http = FakeHttp { call -> HttpReply(201, message.toByteArray()) }
        val api = SocialHttpClient(http, AccountServerScope.parse("https://example.test"))
        val token = "za_" + "A".repeat(43)
        val bytes = byteArrayOf(0, 0, 0, 16, 'f'.code.toByte(), 't'.code.toByte(), 'y'.code.toByte(), 'p'.code.toByte(), 1, 2, 3, 4)
        val voice = api.uploadRecording(token, conversation, "voice", bytes, 1234, null)
        assertEquals(1234, voice.durationMs)
        val call = http.requests.single()
        assertEquals("https://example.test/api/v1/social/conversations/$conversation/voice", call.url)
        assertEquals("Bearer $token", call.headers["Authorization"])
        assertTrue(call.body!!.toString(Charsets.ISO_8859_1).contains("name=\"durationMs\"\r\n\r\n1234"))
        assertTrue(call.body!!.toString(Charsets.ISO_8859_1).contains("filename=\"voice.m4a\""))
        api.uploadRecording(token, conversation, "circle", bytes, 1000, null)
        assertTrue(http.requests.last().url.endsWith("/circles"))
        assertTrue(http.requests.last().body!!.toString(Charsets.ISO_8859_1).contains("filename=\"circle.mp4\""))
    }
    @Test fun recorded_media_rejects_oversized_payload_and_duration_before_network() = runBlocking {
        val http = FakeHttp { error("Must not call transport") }
        val api = SocialHttpClient(http, AccountServerScope.parse("https://example.test"))
        val token = "za_" + "A".repeat(43)
        val conversation = "22222222-2222-4222-8222-222222222222"
        try { api.uploadRecording(token, conversation, "voice", ByteArray(2 * 1024 * 1024 + 1), 1000, null); fail() } catch (_: IllegalArgumentException) { }
        try { api.uploadRecording(token, conversation, "circle", byteArrayOf(1), 60_001, null); fail() } catch (_: IllegalArgumentException) { }
        assertTrue(http.requests.isEmpty())
    }
    @Test fun attachment_download_keeps_bearer_out_of_url() = runBlocking {
        val attachment = "33333333-3333-4333-8333-333333333333"
        val token = "za_" + "A".repeat(43)
        val content = byteArrayOf(1, 2, 3, 4)
        val http = FakeHttp { HttpReply(200, content, "image/png") }
        val api = SocialHttpClient(http, AccountServerScope.parse("https://example.test"))
        assertArrayEquals(content, api.download(token, attachment))
        assertEquals("https://example.test/api/v1/social/attachments/$attachment", http.requests.single().url)
        assertFalse(http.requests.single().url.contains(token))
        assertEquals("Bearer $token", http.requests.single().headers["Authorization"])
    }
}
