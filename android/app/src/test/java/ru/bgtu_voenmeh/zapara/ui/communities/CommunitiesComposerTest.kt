package ru.bgtu_voenmeh.zapara.ui.communities

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.communities.Community
import ru.bgtu_voenmeh.zapara.data.communities.CommunityAnnouncement
import ru.bgtu_voenmeh.zapara.data.communities.CommunityClientFailure
import ru.bgtu_voenmeh.zapara.data.communities.CommunityHomework
import ru.bgtu_voenmeh.zapara.data.communities.CommunityMember
import ru.bgtu_voenmeh.zapara.data.communities.CommunityPoll
import ru.bgtu_voenmeh.zapara.data.communities.HomeworkCompletion
import ru.bgtu_voenmeh.zapara.data.communities.JoinRequest
import ru.bgtu_voenmeh.zapara.data.communities.PollOption
import ru.bgtu_voenmeh.zapara.data.communities.PollOptionTally
import ru.bgtu_voenmeh.zapara.data.communities.PollResults
import ru.bgtu_voenmeh.zapara.data.communities.PollVote
import java.time.Instant

class CommunitiesComposerTest {
    @Test
    fun guest_needs_account_and_ignores_catalog_payload() {
        val state = CommunitiesComposer.compose(
            snapshot(
                guest = true,
                communities = listOf(community("member")),
                selectedId = CID,
                homework = listOf(homework(HID_A, "Задание А")),
                completions = mapOf(HID_A to completion(HID_A, true))
            )
        )
        assertEquals(CommunityPane.Guest, state.pane)
        assertTrue(state.communities.isEmpty())
        assertNull(state.selected)
    }

    @Test
    fun signed_in_empty_catalog_is_empty_pane() {
        val state = CommunitiesComposer.compose(snapshot(guest = false))
        assertEquals(CommunityPane.Empty, state.pane)
        assertTrue(state.communities.isEmpty())
        assertNull(state.selected)
    }

    @Test
    fun forbidden_hides_member_detail() {
        val state = CommunitiesComposer.compose(
            snapshot(
                guest = false,
                failure = CommunityClientFailure.Forbidden,
                communities = listOf(community("member")),
                selectedId = CID,
                homework = listOf(homework(HID_A, "Задание А"))
            )
        )
        assertEquals(CommunityPane.Forbidden, state.pane)
        assertNull(state.selected)
        assertTrue(state.communities.isEmpty())
    }

    @Test
    fun catalog_group_grants_no_role_or_member_actions() {
        val state = CommunitiesComposer.compose(
            snapshot(
                guest = false,
                communities = listOf(community(null)),
                selectedId = CID,
                members = listOf(CommunityMember(UID, "member")),
                joinRequests = listOf(join("pending")),
                homework = listOf(homework(HID_A, "Задание А"))
            )
        )
        assertEquals(CommunityPane.Catalog, state.pane)
        val row = state.communities.single()
        assertNull(row.role)
        assertTrue(row.canJoin)
        assertFalse(row.canOpen)
        assertNull(row.joinStatus)
        assertNull(state.selected)
    }

    @Test
    fun pending_join_hides_join_and_rejected_can_request_again() {
        val pending = CommunitiesComposer.compose(
            snapshot(guest = false, communities = listOf(community(null)), ownJoin = mapOf(CID to join("pending")))
        )
        assertEquals("pending", pending.communities.single().joinStatus)
        assertFalse(pending.communities.single().canJoin)
        val rejected = CommunitiesComposer.compose(
            snapshot(guest = false, communities = listOf(community(null)), ownJoin = mapOf(CID to join("rejected")))
        )
        assertEquals("rejected", rejected.communities.single().joinStatus)
        assertTrue(rejected.communities.single().canJoin)
    }

    @Test
    fun member_personal_completion_is_per_homework_and_not_shared_body() {
        val state = CommunitiesComposer.compose(
            snapshot(
                guest = false,
                communities = listOf(community("member")),
                selectedId = CID,
                members = listOf(CommunityMember(UID, "member")),
                staff = listOf(CommunityMember(STAFF, "headman")),
                joinRequests = listOf(join("pending")),
                homework = listOf(homework(HID_A, "Задание А"), homework(HID_B, "Задание Б")),
                completions = mapOf(
                    HID_A to completion(HID_A, true, 1),
                    HID_B to completion(HID_B, false, 0)
                ),
                announcements = listOf(announcement()),
                polls = listOf(poll()),
                votes = mapOf(PID to PollVote(PID, OID1, AT)),
                results = mapOf(PID to results())
            )
        )
        assertEquals(CommunityPane.Detail, state.pane)
        val detail = requireNotNull(state.selected)
        assertEquals("member", detail.role)
        assertFalse(detail.canModerate)
        assertFalse(detail.canPublish)
        assertTrue(detail.joinRequests.isEmpty())
        assertEquals(listOf(HID_A, HID_B), detail.homework.map { it.homeworkId })
        assertTrue(detail.homework[0].completed)
        assertFalse(detail.homework[1].completed)
        assertEquals("Задание А", detail.homework[0].title)
        assertEquals("Текст", detail.homework[0].body)
        assertTrue(detail.homework[0].canToggle)
        assertEquals(AID, detail.announcements.single().announcementId)
        assertEquals(STAFF, detail.staff.single().userId)
        assertEquals(UID, detail.members.single().userId)
    }

    @Test
    fun staff_can_accept_reject_and_publish() {
        val headman = CommunitiesComposer.compose(staffSnapshot("headman"))
        assertTrue(headman.selected!!.canModerate)
        assertTrue(headman.selected!!.canPublish)
        assertEquals(RID, headman.selected!!.joinRequests.single().requestId)
        assertTrue(headman.selected!!.joinRequests.single().canResolve)
        val curator = CommunitiesComposer.compose(staffSnapshot("curator"))
        assertTrue(curator.selected!!.canModerate)
        assertTrue(curator.selected!!.canPublish)
        assertEquals("curator", curator.selected!!.role)
    }

    @Test
    fun poll_vote_once_before_deadline_then_aggregates_only() {
        val open = CommunitiesComposer.compose(
            memberSnapshot(polls = listOf(poll()), now = AT)
        )
        val openPoll = open.selected!!.polls.single()
        assertFalse(openPoll.closed)
        assertTrue(openPoll.canVote)
        assertNull(openPoll.votedOptionId)
        assertNull(openPoll.results)

        val voted = CommunitiesComposer.compose(
            memberSnapshot(
                polls = listOf(poll()),
                votes = mapOf(PID to PollVote(PID, OID1, AT)),
                results = mapOf(PID to results()),
                now = AT
            )
        )
        val votedPoll = voted.selected!!.polls.single()
        assertFalse(votedPoll.canVote)
        assertEquals(OID1, votedPoll.votedOptionId)
        val tallies = requireNotNull(votedPoll.results)
        assertEquals(1, tallies.totalVotes)
        assertEquals(listOf(OID1 to 1, OID2 to 0), tallies.options.map { it.optionId to it.votes })
        assertEquals(listOf("Да", "Нет"), tallies.options.map { it.label })

        val closed = CommunitiesComposer.compose(
            memberSnapshot(polls = listOf(poll()), results = mapOf(PID to results()), now = DEADLINE)
        )
        val closedPoll = closed.selected!!.polls.single()
        assertTrue(closedPoll.closed)
        assertFalse(closedPoll.canVote)
        assertEquals(1, closedPoll.results!!.totalVotes)
    }

    @Test
    fun rejected_drafts_stay_visible_and_are_not_actionable() {
        val state = CommunitiesComposer.compose(
            memberSnapshot(
                homework = listOf(homework(HID_A, "Задание А")),
                rejectedHomework = listOf(homework(HID_B, "Черновик ДЗ")),
                announcements = listOf(announcement()),
                rejectedAnnouncements = listOf(announcement(AID2, "Черновик", "Отклонено", 0)),
                polls = listOf(poll()),
                rejectedPolls = listOf(poll(PID2, "Черновик опроса"))
            )
        )
        val detail = requireNotNull(state.selected)
        assertEquals(listOf(false, true), detail.homework.map { it.rejectedDraft })
        assertTrue(detail.homework[0].canToggle)
        assertFalse(detail.homework[1].canToggle)
        assertEquals("Черновик ДЗ", detail.homework[1].title)
        assertTrue(detail.announcements.any { it.rejectedDraft && it.title == "Черновик" })
        assertTrue(detail.polls.any { it.rejectedDraft && !it.canVote })
    }

    private fun staffSnapshot(role: String) = snapshot(
        guest = false,
        communities = listOf(community(role)),
        selectedId = CID,
        members = listOf(CommunityMember(UID, "member")),
        staff = listOf(CommunityMember(STAFF, role)),
        joinRequests = listOf(join("pending")),
        homework = listOf(homework(HID_A, "Задание А"))
    )

    private fun memberSnapshot(
        homework: List<CommunityHomework> = emptyList(),
        completions: Map<String, HomeworkCompletion> = emptyMap(),
        announcements: List<CommunityAnnouncement> = emptyList(),
        polls: List<CommunityPoll> = emptyList(),
        votes: Map<String, PollVote> = emptyMap(),
        results: Map<String, PollResults> = emptyMap(),
        rejectedHomework: List<CommunityHomework> = emptyList(),
        rejectedAnnouncements: List<CommunityAnnouncement> = emptyList(),
        rejectedPolls: List<CommunityPoll> = emptyList(),
        now: Instant = AT
    ) = snapshot(
        guest = false,
        communities = listOf(community("member")),
        selectedId = CID,
        members = listOf(CommunityMember(UID, "member")),
        homework = homework,
        completions = completions,
        announcements = announcements,
        polls = polls,
        votes = votes,
        results = results,
        rejectedHomework = rejectedHomework,
        rejectedAnnouncements = rejectedAnnouncements,
        rejectedPolls = rejectedPolls,
        now = now
    )

    private fun snapshot(
        guest: Boolean,
        failure: CommunityClientFailure? = null,
        communities: List<Community> = emptyList(),
        ownJoin: Map<String, JoinRequest> = emptyMap(),
        selectedId: String? = null,
        members: List<CommunityMember> = emptyList(),
        staff: List<CommunityMember> = emptyList(),
        joinRequests: List<JoinRequest> = emptyList(),
        homework: List<CommunityHomework> = emptyList(),
        completions: Map<String, HomeworkCompletion> = emptyMap(),
        announcements: List<CommunityAnnouncement> = emptyList(),
        polls: List<CommunityPoll> = emptyList(),
        votes: Map<String, PollVote> = emptyMap(),
        results: Map<String, PollResults> = emptyMap(),
        rejectedHomework: List<CommunityHomework> = emptyList(),
        rejectedAnnouncements: List<CommunityAnnouncement> = emptyList(),
        rejectedPolls: List<CommunityPoll> = emptyList(),
        now: Instant = AT
    ) = CommunitySnapshot(
        guest = guest,
        failure = failure,
        communities = communities,
        ownJoin = ownJoin,
        selectedId = selectedId,
        members = members,
        staff = staff,
        joinRequests = joinRequests,
        homework = homework,
        completions = completions,
        announcements = announcements,
        polls = polls,
        votes = votes,
        results = results,
        rejectedHomework = rejectedHomework,
        rejectedAnnouncements = rejectedAnnouncements,
        rejectedPolls = rejectedPolls,
        now = now
    )

    private fun community(role: String?) = Community(CID, "О3313", "Группа А863С", 1, role)
    private fun join(status: String) = JoinRequest(RID, CID, UID, status, AT)
    private fun homework(id: String, title: String) = CommunityHomework(id, CID, title, "Текст", 1, AT, AT)
    private fun completion(id: String, done: Boolean, revision: Long = if (done) 1 else 0) =
        HomeworkCompletion(id, done, revision, if (done) AT else null)
    private fun announcement(
        id: String = AID,
        title: String = "Собрание",
        body: String = "В 18:00",
        revision: Long = 1
    ) = CommunityAnnouncement(id, CID, title, body, revision, AT, AT)
    private fun poll(id: String = PID, question: String = "Придете?") = CommunityPoll(
        id, CID, question, DEADLINE, 1,
        listOf(PollOption(OID1, "Да", 0), PollOption(OID2, "Нет", 1))
    )
    private fun results() = PollResults(
        PID, 1,
        listOf(PollOptionTally(OID1, "Да", 1), PollOptionTally(OID2, "Нет", 0))
    )
}

private const val CID = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
private const val HID_A = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
private const val HID_B = "cccccccc-cccc-4ccc-8ccc-cccccccccccc"
private const val UID = "dddddddd-dddd-4ddd-8ddd-dddddddddddd"
private const val STAFF = "ffffffff-ffff-4fff-8fff-ffffffffffff"
private const val RID = "11111111-1111-4111-8111-111111111111"
private const val PID = "33333333-3333-4333-8333-333333333333"
private const val OID1 = "44444444-4444-4444-8444-444444444444"
private const val OID2 = "55555555-5555-4555-8555-555555555555"
private const val AID = "66666666-6666-4666-8666-666666666666"
private const val AID2 = "77777777-7777-4777-8777-777777777777"
private const val PID2 = "88888888-8888-4888-8888-888888888888"
private val AT: Instant = Instant.parse("2026-09-08T12:00:00Z")
private val DEADLINE: Instant = Instant.parse("2026-09-08T12:10:00Z")
