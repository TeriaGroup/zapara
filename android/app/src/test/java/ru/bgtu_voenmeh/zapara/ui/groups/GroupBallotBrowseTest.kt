package ru.bgtu_voenmeh.zapara.ui.groups

import java.time.Instant
import org.junit.Assert.assertEquals
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.communities.Ballot
import ru.bgtu_voenmeh.zapara.data.communities.BallotOption

class GroupBallotBrowseTest {
    private val later = ballot("later", "Когда будет встреча?", "open", "2026-10-06T12:00:00Z", "Во вторник")
    private val tied = ballot("tied", "Куда идти?", "collecting", "2026-10-06T12:00:00Z", "В корпус")
    private val earlier = ballot("earlier", "Когда сдавать?", "closed", "2026-10-02T12:00:00Z", "Завтра")

    @Test fun searchMatchesQuestionOrOptionOnCurrentBoardOnly() {
        val board = listOf(later, tied, earlier)
        assertEquals(listOf("later"), browseBallots(board, "  вСтРеЧа  ").map { it.ballotId })
        assertEquals(listOf("tied"), browseBallots(board, "  КОРПУС ").map { it.ballotId })
        assertEquals(emptyList<Ballot>(), browseBallots(emptyList(), "встреча"))
        assertEquals(listOf("later", "tied", "earlier"), board.map { it.ballotId })
    }

    @Test fun statusComesFromServerAndLiveReplacementRecomputesVisibleBoard() {
        val firstBoard = listOf(later, tied)
        assertEquals(listOf("later"), browseBallots(firstBoard, "", BallotStatus.Open).map { it.ballotId })
        val refreshed = listOf(later.copy(status = "closed"), tied.copy(status = "open"))
        assertEquals(listOf("tied"), browseBallots(refreshed, "", BallotStatus.Open).map { it.ballotId })
        assertEquals(listOf("earlier"), browseBallots(listOf(earlier), "", BallotStatus.Closed).map { it.ballotId })
    }

    @Test fun deadlineSortIsStableForTiesAndLeavesOriginalOrderUntouched() {
        val board = listOf(later, tied, earlier)
        assertEquals(listOf("later", "tied", "earlier"), browseBallots(board, "", sort = BallotSort.Original).map { it.ballotId })
        assertEquals(listOf("earlier", "later", "tied"), browseBallots(board, "", sort = BallotSort.Nearest).map { it.ballotId })
        assertEquals(listOf("later", "tied", "earlier"), browseBallots(board, "", sort = BallotSort.Farthest).map { it.ballotId })
        assertEquals(listOf("later", "tied", "earlier"), board.map { it.ballotId })
    }

    @Test fun resultPercentUsesVotesNotMembersWithHalfUpRounding() {
        assertEquals(0, ballotPercent(0, 0))
        assertEquals(13, ballotPercent(1, 8))
        assertEquals(67, ballotPercent(2, 3))
        assertEquals(100, ballotPercent(8, 8))
    }

    @Test fun copiedSummaryContainsOnlyPublicResultsAndSamePercentMath() {
        val item = earlier.copy(options = listOf(
            BallotOption("one", "Завтра", 1, false),
            BallotOption("two", "Послезавтра", 7, true)))
        val summary = ballotSummary(item, "Статус: завершено", "Срок: 02.10.2026 15:00", "Голоса", "Доля голосов")
        assertEquals("Когда сдавать?\nСтатус: завершено\nСрок: 02.10.2026 15:00\nЗавтра — Голоса: 1; Доля голосов: 13%\nПослезавтра — Голоса: 7; Доля голосов: 88%", summary)
    }

    @Test fun collectingSummaryIncludesSupportWithoutVoterIdentity() {
        val item = tied.copy(supporters = 2, supportersNeeded = 3)
        val summary = ballotSummary(item, "Статус: сбор поддержки", "Срок: 06.10.2026 15:00",
            "Голоса", "Доля голосов", "Поддержали 2 из 3")
        assertEquals("Куда идти?\nСтатус: сбор поддержки\nСрок: 06.10.2026 15:00\nПоддержали 2 из 3\nВ корпус — Голоса: 0; Доля голосов: 0%", summary)
    }

    @Test fun nearDeadlineNoticeNeverChangesServerStatus() {
        val now = Instant.parse("2026-10-06T10:00:00Z")
        assertEquals(DeadlineNotice.Soon, ballotDeadlineNotice(later, now))
        assertEquals(DeadlineNotice.AwaitingServer, ballotDeadlineNotice(later.copy(deadlineAt = now.minusSeconds(60)), now))
        assertEquals(DeadlineNotice.None, ballotDeadlineNotice(later.copy(status = "closed"), now))
        assertEquals("open", later.status)
    }

    private fun ballot(id: String, question: String, status: String, deadline: String, option: String) = Ballot(
        id, question, "headman", status, Instant.parse(deadline), 0, 3, false,
        listOf(BallotOption("$id-option", option, 0, false)), "", "", null
    )
}
