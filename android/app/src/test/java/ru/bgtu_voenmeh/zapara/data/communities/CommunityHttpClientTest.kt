package ru.bgtu_voenmeh.zapara.data.communities

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.api.FakeHttp
import ru.bgtu_voenmeh.zapara.data.api.HttpCall
import ru.bgtu_voenmeh.zapara.data.api.HttpReply
import java.time.Instant

class CommunityHttpClientTest {
    @Test
    fun catalog_groupId_query_grants_no_membership() = runBlocking {
        val http = FakeHttp { call ->
            assertEquals("GET", call.method)
            assertEquals("$BASE?groupId=O3313", call.url)
            assertNull(call.body)
            assertEquals("Bearer $ACCESS", call.headers["Authorization"])
            ok(arr(communityJson(role = null)))
        }
        val found = client(http).list(ACCESS, "O3313")
        assertEquals(1, found.size)
        assertEquals(CID, found[0].communityId)
        assertNull(found[0].role)
        assertEquals(1, http.requests.size)
    }

    @Test
    fun member_reads_and_personal_completion_use_distinct_homework_urls() = runBlocking {
        val http = FakeHttp { call ->
            when (suffix(call)) {
                "GET " -> ok(arr(communityJson("member")))
                "GET /$CID" -> ok(communityJson("member"))
                "POST /$CID/join-requests" -> {
                    assertNull(call.headers["Content-Type"])
                    assertTrue(call.body.let { it == null || it.isEmpty() })
                    created(joinJson("pending"))
                }
                "GET /$CID/members" -> ok(arr(memberJson("member")))
                "GET /$CID/staff" -> ok(arr(memberJson("headman", STAFF)))
                "GET /$CID/homework" -> ok(arr(homeworkJson(HID_A, "Задание А"), homeworkJson(HID_B, "Задание Б")))
                "GET /$CID/homework/$HID_A" -> ok(homeworkJson(HID_A, "Задание А"))
                "GET /$CID/homework/$HID_A/completion" -> ok(completionJson(HID_A, true, 1, AT))
                "GET /$CID/homework/$HID_B/completion" -> ok(completionJson(HID_B, false, 0, null))
                "PUT /$CID/homework/$HID_A/completion" -> {
                    assertEquals("""{"completed":true,"expectedRevision":0}""", text(call))
                    ok(completionJson(HID_A, true, 1, AT))
                }
                "GET /$CID/announcements" -> ok(arr(announcementJson()))
                "GET /$CID/polls" -> ok(arr(pollJson()))
                "GET /$CID/polls/$PID" -> ok(pollJson())
                "POST /$CID/polls/$PID/votes" -> {
                    assertEquals("""{"optionId":"$OID1"}""", text(call))
                    created(voteJson())
                }
                "GET /$CID/polls/$PID/results" -> ok(resultsJson())
                else -> error(suffix(call))
            }
        }
        val api = client(http)
        assertEquals("member", api.list(ACCESS).single().role)
        assertEquals("member", api.get(ACCESS, CID).role)
        assertEquals("pending", api.requestJoin(ACCESS, CID).status)
        assertEquals("member", api.listMembers(ACCESS, CID).single().role)
        assertEquals(STAFF, api.listStaff(ACCESS, CID).single().userId)
        val homework = api.listHomework(ACCESS, CID)
        assertEquals(listOf(HID_A, HID_B), homework.map { it.homeworkId })
        assertEquals("Задание А", api.getHomework(ACCESS, CID, HID_A).title)
        val done = api.upsertCompletion(ACCESS, CID, HID_A, true, 0)
        assertTrue(done.completed)
        assertEquals(HID_A, done.homeworkId)
        val other = api.getCompletion(ACCESS, CID, HID_B)
        assertFalse(other.completed)
        assertEquals(0, other.revision)
        assertNull(other.updatedAt)
        assertEquals(HID_B, other.homeworkId)
        assertEquals(AID, api.listAnnouncements(ACCESS, CID).single().announcementId)
        assertEquals(PID, api.listPolls(ACCESS, CID).single().pollId)
        assertEquals(2, api.getPoll(ACCESS, CID, PID).options.size)
        assertEquals(OID1, api.vote(ACCESS, CID, PID, OID1).optionId)
        val results = api.results(ACCESS, CID, PID)
        assertEquals(1, results.totalVotes)
        assertEquals(setOf(OID1, OID2), results.options.map { it.optionId }.toSet())
        assertTrue(http.requests.none { it.url.contains("/votes") && it.method == "GET" })
        assertTrue(http.requests.any { it.url.endsWith("/homework/$HID_A/completion") && it.method == "PUT" })
        assertTrue(http.requests.any { it.url.endsWith("/homework/$HID_B/completion") && it.method == "GET" })
        assertTrue(http.requests.none { it.url.endsWith("/homework/$HID_B") && it.method == "PUT" })
        assertFalse(homeworkJson(HID_A, "Задание А").contains("completed"))
    }

    @Test
    fun staff_join_publish_and_accept_return_server_payload_without_rewriting_roles() = runBlocking {
        val http = FakeHttp { call ->
            when (suffix(call)) {
                "GET /$CID/join-requests" -> ok(arr(joinJson("pending")))
                "POST /$CID/join-requests/$RID/accept" -> {
                    assertTrue(call.body.let { it == null || it.isEmpty() })
                    ok(joinJson("accepted"))
                }
                "POST /$CID/join-requests/$RID/reject" -> {
                    assertTrue(call.body.let { it == null || it.isEmpty() })
                    ok(joinJson("rejected", RID2, UID2))
                }
                "POST /$CID/homework" -> {
                    assertEquals("""{"title":"ДЗ","body":"Текст","expectedRevision":0}""", text(call))
                    created(homeworkJson(HID_A, "ДЗ", "Текст", 1))
                }
                "PUT /$CID/homework/$HID_A" -> {
                    assertEquals("""{"title":"ДЗ","body":"Правка","expectedRevision":1}""", text(call))
                    ok(homeworkJson(HID_A, "ДЗ", "Правка", 2))
                }
                "POST /$CID/announcements" -> created(announcementJson("Объявление", "Текст", 1))
                "PUT /$CID/announcements/$AID" -> ok(announcementJson("Объявление", "Правка", 2))
                "POST /$CID/polls" -> {
                    assertEquals(
                        """{"question":"Придете?","deadlineAt":"$DEADLINE","options":["Да","Нет"],"expectedRevision":0}""",
                        text(call)
                    )
                    created(pollJson())
                }
                "GET /$CID/members" -> ok(arr(memberJson("curator", UID)))
                else -> error(suffix(call))
            }
        }
        val api = client(http)
        assertEquals("pending", api.listJoinRequests(ACCESS, CID).single().status)
        assertEquals("accepted", api.acceptJoin(ACCESS, CID, RID).status)
        assertEquals("rejected", api.rejectJoin(ACCESS, CID, RID).status)
        assertEquals(1, api.publishHomework(ACCESS, CID, "ДЗ", "Текст", 0).revision)
        assertEquals(2, api.updateHomework(ACCESS, CID, HID_A, "ДЗ", "Правка", 1).revision)
        assertEquals(1, api.publishAnnouncement(ACCESS, CID, "Объявление", "Текст", 0).revision)
        assertEquals(2, api.updateAnnouncement(ACCESS, CID, AID, "Объявление", "Правка", 1).revision)
        assertEquals(PID, api.publishPoll(ACCESS, CID, "Придете?", Instant.parse(DEADLINE), listOf("Да", "Нет"), 0).pollId)
        val members = api.listMembers(ACCESS, CID)
        assertEquals("curator", members.single().role)
        assertEquals(UID, members.single().userId)
    }

    @Test
    fun member_publish_is_forbidden_and_vote_conflicts_are_typed() = runBlocking {
        var votes = 0
        val http = FakeHttp { call ->
            when (suffix(call)) {
                "POST /$CID/homework" -> problem(403, "forbidden")
                "POST /$CID/announcements" -> problem(403, "forbidden")
                "POST /$CID/polls" -> problem(403, "forbidden")
                "POST /$CID/polls/$PID/votes" -> {
                    votes++
                    if (votes == 1) created(voteJson()) else problem(409, "already_voted")
                }
                "GET /$CID/polls/$PID/results" -> ok(
                    """{"pollId":"$PID","totalVotes":1,"options":[{"optionId":"$OID1","label":"Да","votes":1},{"optionId":"$OID2","label":"Нет","votes":0}]}"""
                )
                else -> error(suffix(call))
            }
        }
        val api = client(http)
        expect(CommunityClientFailure.Forbidden) { api.publishHomework(ACCESS, CID, "ДЗ", "Текст", 0) }
        expect(CommunityClientFailure.Forbidden) { api.publishAnnouncement(ACCESS, CID, "Нет", "Текст", 0) }
        expect(CommunityClientFailure.Forbidden) {
            api.publishPoll(ACCESS, CID, "Придете?", Instant.parse(DEADLINE), listOf("Да", "Нет"), 0)
        }
        assertEquals(OID1, api.vote(ACCESS, CID, PID, OID1).optionId)
        expect(CommunityClientFailure.AlreadyVoted) { api.vote(ACCESS, CID, PID, OID2) }
        val results = api.results(ACCESS, CID, PID)
        assertEquals(1, results.totalVotes)
        assertEquals(1, results.options.first { it.optionId == OID1 }.votes)
        assertEquals(0, results.options.first { it.optionId == OID2 }.votes)
        assertTrue(results.options.none { it.label.contains("user", ignoreCase = true) })
    }

    @Test
    fun poll_closed_and_revision_conflict_and_session_are_typed() = runBlocking {
        val http = FakeHttp { call ->
            when (suffix(call)) {
                "POST /$CID/polls/$PID/votes" -> problem(409, "poll_closed")
                "PUT /$CID/homework/$HID_A" -> problem(409, "revision_conflict")
                "GET /$CID/homework" -> problem(401, "invalid_session")
                "GET /$CID/members" -> problem(404, "not_found")
                "POST /$CID/join-requests" -> problem(409, "already_member")
                else -> error(suffix(call))
            }
        }
        val api = client(http)
        expect(CommunityClientFailure.PollClosed) { api.vote(ACCESS, CID, PID, OID1) }
        expect(CommunityClientFailure.RevisionConflict) { api.updateHomework(ACCESS, CID, HID_A, "ДЗ", "Нет", 0) }
        expect(CommunityClientFailure.InvalidSession) { api.listHomework(ACCESS, CID) }
        expect(CommunityClientFailure.NotFound) { api.listMembers(ACCESS, CID) }
        expect(CommunityClientFailure.AlreadyMember) { api.requestJoin(ACCESS, CID) }
    }

    @Test
    fun results_reject_individual_vote_identities() = runBlocking {
        val http = FakeHttp {
            ok("""{"pollId":"$PID","totalVotes":1,"userId":"$UID","options":[{"optionId":"$OID1","label":"Да","votes":1},{"optionId":"$OID2","label":"Нет","votes":0}]}""")
        }
        expect(CommunityClientFailure.InvalidPayload) { client(http).results(ACCESS, CID, PID) }
        val leaked = FakeHttp {
            ok("""{"pollId":"$PID","totalVotes":1,"options":[{"optionId":"$OID1","label":"Да","votes":1,"userId":"$UID"},{"optionId":"$OID2","label":"Нет","votes":0}]}""")
        }
        expect(CommunityClientFailure.InvalidPayload) { client(leaked).results(ACCESS, CID, PID) }
    }

    @Test
    fun homework_payload_does_not_carry_personal_completion() = runBlocking {
        val http = FakeHttp {
            ok(arr("""{"homeworkId":"$HID_A","communityId":"$CID","title":"ДЗ","body":"Текст","revision":1,"createdAt":"$AT","updatedAt":"$AT","completed":true}"""))
        }
        expect(CommunityClientFailure.InvalidPayload) { client(http).listHomework(ACCESS, CID) }
    }

    @Test
    fun cyrillic_homework_body_may_exceed_account_16kib_write_cap() = runBlocking {
        val title = "Я".repeat(200)
        val body = "Я".repeat(8000)
        val http = FakeHttp { call ->
            val sent = call.body!!
            assertTrue(sent.size > 16384)
            assertTrue(sent.size <= CommunityValidation.RequestBytes)
            assertTrue(String(sent, Charsets.UTF_8).contains(body))
            created(homeworkJson(HID_A, title, body, 1))
        }
        val published = client(http).publishHomework(ACCESS, CID, title, body, 0)
        assertEquals(body, published.body)
        try {
            client(http).publishHomework(ACCESS, CID, title, body + "!", 0)
            fail()
        } catch (e: IllegalArgumentException) {
            assertEquals("Недопустимый контракт сообщества.", e.message)
        }
        assertEquals(1, http.requests.size)
    }

    private suspend fun expect(failure: CommunityClientFailure, block: suspend () -> Unit) {
        try {
            block()
            fail(failure.name)
        } catch (e: CommunityClientException) {
            assertEquals(failure, e.failure)
            assertEquals("Операция сообщества не выполнена.", e.message)
        }
    }
}

private val SCOPE = AccountServerScope.parse("https://example.invalid/root")
private val ACCESS: String = run {
    val encoded = java.util.Base64.getUrlEncoder().withoutPadding().encodeToString(ByteArray(32) { 1 })
    "za_$encoded"
}
private const val BASE = "https://example.invalid/root/api/v1/communities"
private const val CID = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
private const val HID_A = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
private const val HID_B = "cccccccc-cccc-4ccc-8ccc-cccccccccccc"
private const val UID = "dddddddd-dddd-4ddd-8ddd-dddddddddddd"
private const val UID2 = "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee"
private const val STAFF = "ffffffff-ffff-4fff-8fff-ffffffffffff"
private const val RID = "11111111-1111-4111-8111-111111111111"
private const val RID2 = "22222222-2222-4222-8222-222222222222"
private const val PID = "33333333-3333-4333-8333-333333333333"
private const val OID1 = "44444444-4444-4444-8444-444444444444"
private const val OID2 = "55555555-5555-4555-8555-555555555555"
private const val AID = "66666666-6666-4666-8666-666666666666"
private const val AT = "2026-09-08T12:00:00Z"
private const val DEADLINE = "2026-09-08T12:10:00Z"

private fun client(http: FakeHttp) = CommunityHttpClient(http, SCOPE)
private fun suffix(call: HttpCall) = call.method + " " + call.url.removePrefix(BASE)
private fun text(call: HttpCall): String = String(call.body!!, Charsets.UTF_8)
private fun ok(body: String) = HttpReply(200, body.toByteArray())
private fun created(body: String) = HttpReply(201, body.toByteArray())
private fun problem(status: Int, code: String) =
    HttpReply(status, """{"title":"Ошибка","status":$status,"code":"$code"}""".toByteArray())
private fun arr(vararg items: String) = items.joinToString(",", "[", "]")
private fun communityJson(role: String?, id: String = CID) =
    """{"communityId":"$id","name":"Группа О3313","description":"","revision":1,"role":${if (role == null) "null" else "\"$role\""}}"""
private fun joinJson(status: String, requestId: String = RID, userId: String = UID) =
    """{"requestId":"$requestId","communityId":"$CID","userId":"$userId","status":"$status","createdAt":"$AT"}"""
private fun memberJson(role: String, userId: String = UID) = """{"userId":"$userId","role":"$role"}"""
private fun homeworkJson(id: String, title: String, body: String = "Текст", revision: Long = 1) =
    """{"homeworkId":"$id","communityId":"$CID","title":"$title","body":"$body","revision":$revision,"createdAt":"$AT","updatedAt":"$AT"}"""
private fun completionJson(id: String, completed: Boolean, revision: Long, updatedAt: String?) =
    """{"homeworkId":"$id","completed":$completed,"revision":$revision,"updatedAt":${if (updatedAt == null) "null" else "\"$updatedAt\""}}"""
private fun announcementJson(title: String = "Объявление", body: String = "Текст", revision: Long = 1) =
    """{"announcementId":"$AID","communityId":"$CID","title":"$title","body":"$body","revision":$revision,"createdAt":"$AT","updatedAt":"$AT"}"""
private fun pollJson() =
    """{"pollId":"$PID","communityId":"$CID","question":"Придете?","deadlineAt":"$DEADLINE","revision":1,"options":[{"optionId":"$OID1","label":"Да","ordinal":1},{"optionId":"$OID2","label":"Нет","ordinal":2}]}"""
private fun voteJson() = """{"pollId":"$PID","optionId":"$OID1","createdAt":"$AT"}"""
private fun resultsJson() =
    """{"pollId":"$PID","totalVotes":1,"options":[{"optionId":"$OID1","label":"Да","votes":1},{"optionId":"$OID2","label":"Нет","votes":0}]}"""
