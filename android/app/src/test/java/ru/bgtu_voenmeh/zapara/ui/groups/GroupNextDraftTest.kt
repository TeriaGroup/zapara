package ru.bgtu_voenmeh.zapara.ui.groups

import java.time.Instant
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.communities.Ballot
import ru.bgtu_voenmeh.zapara.data.communities.BallotBoard
import ru.bgtu_voenmeh.zapara.data.communities.GroupFormDraft
import ru.bgtu_voenmeh.zapara.data.communities.GroupFormQuestion

class GroupNextDraftTest {
    @Test fun quote_lookup_uses_only_loaded_rows_and_marks_deleted_target() {
        val rows = listOf(GroupMessageUi("one", "", "", "", false),
            GroupMessageUi("two", "", "", "", false, deleted = true))
        assertEquals(QuoteTarget.Loaded, quoteTarget(rows, "one"))
        assertEquals(QuoteTarget.Deleted, quoteTarget(rows, "two"))
        assertEquals(QuoteTarget.Earlier, quoteTarget(rows, "other"))
    }

    @Test fun reverse_ballot_ack_updates_only_owned_row_and_filters_topic() {
        fun ballot(id: String, count: Int, topic: String = "topic") = Ballot(id, id, "collective", "open",
            Instant.parse("2026-10-04T12:00:00Z"), count, 3, false, emptyList(), "", "", topic)
        fun board(vararg rows: Ballot) = BallotBoard(false, false, false, 20, 3, rows.toList())
        val initial = board(ballot("a", 0), ballot("b", 0))
        val first = mergeBallotPost(initial, board(ballot("a", 0), ballot("b", 1)), "topic", "b")
        val reversed = mergeBallotPost(first, board(ballot("a", 1), ballot("b", 0), ballot("else", 2, "other")), "topic", "a")
        assertEquals(listOf(1, 1), reversed.ballots.map { it.supporters })
        assertEquals(listOf("a", "b"), reversed.ballots.map { it.ballotId })
    }

    @Test fun question_reorder_preserves_ids_content_and_wire_text_options_are_empty() {
        val a = GroupFormQuestion("a", "Первый", "singleChoice", true, listOf("Да", "Нет"))
        val b = GroupFormQuestion("b", "Второй", "longText", false, listOf("сохранённый выбор"))
        val moved = moveQuestion(listOf(a, b), 1, -1)
        assertEquals(listOf("b", "a"), moved.map { it.questionId })
        assertEquals(b, moved[0])
        assertEquals(listOf(a, b), moveQuestion(listOf(a, b), 0, -1))
        assertTrue(questionHasDraft(a))
        assertFalse(questionHasDraft(GroupFormQuestion("empty", "", "shortText", false, emptyList())))
        assertEquals(emptyList<String>(), GroupFormDraft("Анкета", "", null, false, moved).normalized().questions[0].options)
    }
}
