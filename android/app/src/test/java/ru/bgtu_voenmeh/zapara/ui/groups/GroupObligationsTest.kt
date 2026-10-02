package ru.bgtu_voenmeh.zapara.ui.groups

import org.junit.Assert.*
import org.junit.Test
import java.time.Instant
import kotlinx.coroutines.runBlocking
import ru.bgtu_voenmeh.zapara.data.api.*
import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.communities.*

class GroupObligationsTest {
    private val community = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
    private val topicId = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
    private val token = "za_" + java.util.Base64.getUrlEncoder().withoutPadding().encodeToString(ByteArray(32) { 1 })
    private fun topic(id: String, readable: Boolean = true) = GroupTopic(id, "Тема", "", "forms", null, null, null, 0,
        false, 0, permissions = if (readable) listOf("read") else emptyList())
    @Test fun incidental_unread_poll_does_not_invalidate_authority_but_revocation_does() {
        val original = listOf(topic(topicId))
        assertEquals(obligationAuthority(original), obligationAuthority(original.map { it.copy(unread = 10, lastBody = "новое") }))
        assertNotEquals(obligationAuthority(original), obligationAuthority(original.map { it.copy(permissions = emptyList()) }))
    }
    @Test fun collector_never_requests_unreadable_topics_and_reports_partial_failure() = runBlocking {
        val calls = mutableListOf<HttpCall>()
        val api = CommunityHttpClient(HttpExchange { call ->
            calls += call
            if (call.url.endsWith("/ballots")) HttpReply(200, emptyBoard.toByteArray())
            else if (call.url.contains(topicId)) HttpReply(200, "{\"forms\":[]}".toByteArray())
            else HttpReply(503, "{\"title\":\"Ошибка\",\"status\":503,\"code\":\"db_unavailable\"}".toByteArray())
        }, AccountServerScope.parse("https://example.invalid/root"))
        val hidden = "cccccccc-cccc-4ccc-8ccc-cccccccccccc"
        val broken = "dddddddd-dddd-4ddd-8ddd-dddddddddddd"
        val result = collectGroupObligations(api, token, community, listOf(topic(topicId), topic(hidden, false), topic(broken))) { true }!!
        assertEquals(2, result.loaded)
        assertEquals(1, result.failed)
        assertEquals(3, calls.size)
        assertTrue(calls.all { it.method == "GET" && !it.url.contains(hidden) })
        assertEquals(1, result.skipped)
    }
    private val emptyBoard = """{"headman":false,"canOpen":false,"canClose":false,"members":2,"supportersNeeded":1,"ballots":[]}"""
    @Test fun many_homework_topics_share_two_reads_without_per_item_fallback() = runBlocking {
        val calls = mutableListOf<HttpCall>()
        val api = CommunityHttpClient(HttpExchange { call ->
            calls += call
            HttpReply(200, (if (call.url.endsWith("/ballots")) emptyBoard else "[]").toByteArray())
        }, AccountServerScope.parse("https://example.invalid/root"))
        val topics = (1..24).map { topic(java.util.UUID.nameUUIDFromBytes(it.toString().toByteArray()).toString()).copy(kind = "homework") }
        val result = collectGroupObligations(api, token, community, topics) { true }!!
        assertEquals(25, result.requested)
        assertEquals(25, result.loaded)
        assertEquals(3, calls.size)
        assertTrue(calls.none { it.url.contains("completion/") })
    }
    @Test fun changed_scope_drops_late_response_without_next_request() = runBlocking {
        var current = true
        var calls = 0
        val api = CommunityHttpClient(HttpExchange {
            calls++; current = false; HttpReply(200, "{\"forms\":[]}".toByteArray())
        }, AccountServerScope.parse("https://example.invalid/root"))
        assertNull(collectGroupObligations(api, token, community, listOf(topic(topicId),
            topic("dddddddd-dddd-4ddd-8ddd-dddddddddddd"))) { current })
        assertEquals(1, calls)
    }
    @Test fun revoked_access_is_not_reported_as_an_empty_or_partial_agenda() = runBlocking {
        val api = CommunityHttpClient(HttpExchange {
            HttpReply(403, "{\"title\":\"Ошибка\",\"status\":403,\"code\":\"forbidden\"}".toByteArray())
        }, AccountServerScope.parse("https://example.invalid/root"))
        try {
            collectGroupObligations(api, token, community, listOf(topic(topicId))) { true }
            fail("Access denial must leave the aggregation path")
        } catch (error: CommunityClientException) { assertEquals(CommunityClientFailure.Forbidden, error.failure) }
    }
    @Test fun needs_me_keeps_overdue_tasks_but_not_closed_response_windows() {
        val now = Instant.parse("2026-10-02T10:00:00Z")
        val rows = listOf(GroupObligation("h", "t", "homework", "Задание", now.minusSeconds(1), true),
            GroupObligation("f", "t", "forms", "Анкета", now.minusSeconds(1), true),
            GroupObligation("b", "t", "ballots", "Опрос", now.plusSeconds(1), true),
            GroupObligation("done", "t", "homework", "Готово", null, false))
        assertEquals(listOf("h", "b"), browseObligations(rows, true, now).map { it.id })
        assertEquals(rows, browseObligations(rows, false, now))
    }
}
