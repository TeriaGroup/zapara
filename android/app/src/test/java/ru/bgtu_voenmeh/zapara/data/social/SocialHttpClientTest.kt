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
        assertEquals("Личный чат · ИВТ-1", rows.first().subtitle)
        assertEquals("Учебная группа", rows.last().subtitle)
    }
    @Test fun inbox_orders_latest_then_unread_and_keeps_empty_chats() {
        val rows = listOf(InboxRow("a", "А", lastAt = java.time.Instant.parse("2026-01-01T00:00:00Z")), InboxRow("b", "Б", unread = 1), InboxRow("c", "В"))
        assertEquals(listOf("a", "b", "c"), orderInbox(rows).map { it.id })
    }
}
