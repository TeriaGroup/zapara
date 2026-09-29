package ru.bgtu_voenmeh.zapara.data.communities

import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.api.FakeHttp
import ru.bgtu_voenmeh.zapara.data.api.HttpReply
import java.time.Instant

class GroupChatUpdatesTest {
    private fun message(index: Int) = ChatMessage(index.toString(), "chat", "user", "Имя", "Текст", Instant.EPOCH.plusSeconds(index.toLong()))
    @Test fun receives_all_intermediate_pages_before_advancing_cursor() = runBlocking {
        val requested = mutableListOf<String?>()
        val result = loadGroupChatUpdates("0") { after ->
            requested += after
            val start = after!!.toInt() + 1
            val end = minOf(start + 49, 130)
            ChatPage((start..end).map(::message), end < 130)
        }
        assertEquals(listOf("0", "50", "100"), requested)
        assertEquals((1..130).map(Int::toString), result.messages.map { it.messageId })
        assertFalse(result.hasMore)
    }
    @Test fun bounded_catchup_keeps_actual_cursor_and_rejects_repeating_page() = runBlocking {
        val partial = loadGroupChatUpdates("0", maxPages = 2) { after ->
            val next = after!!.toInt() + 1
            ChatPage(listOf(message(next)), true)
        }
        assertEquals("2", partial.messages.last().messageId)
        assertTrue(partial.hasMore)
        try { loadGroupChatUpdates("1") { ChatPage(listOf(message(1)), true) }; fail() }
        catch (error: CommunityClientException) { assertEquals(CommunityClientFailure.InvalidPayload, error.failure) }
    }
    @Test fun explicit_read_header_and_target_are_sent_without_dto_changes() = runBlocking {
        val id = "11111111-1111-4111-8111-111111111111"
        val target = "22222222-2222-4222-8222-222222222222"
        val http = FakeHttp { call -> HttpReply(200, (if (call.method == "GET") """{"messages":[],"hasMore":false}""" else
            """{"conversationId":"$id","kind":"group","communityId":"$id","title":"Группа","peerUserId":null,"lastBody":null,"lastAt":null,"unread":2}""").toByteArray()) }
        val api = CommunityHttpClient(http, AccountServerScope.parse("https://example.test"))
        val token = "za_" + "A".repeat(43)
        api.messages(token, id)
        assertEquals("1", http.requests.last().headers["X-Zapara-Read-Cursor"])
        api.markRead(token, id, target)
        assertEquals("""{"throughMessageId":"$target"}""", http.requests.last().body!!.toString(Charsets.UTF_8))
        api.markRead(token, id)
        assertNull(http.requests.last().body)
    }
}
