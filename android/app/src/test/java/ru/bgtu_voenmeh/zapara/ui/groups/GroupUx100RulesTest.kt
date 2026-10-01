package ru.bgtu_voenmeh.zapara.ui.groups

import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.communities.GroupFormAnswer
import ru.bgtu_voenmeh.zapara.data.communities.GroupFormQuestion

class GroupUx100RulesTest {
    @Test fun duplicated_question_is_independent_and_inserted_after_source() {
        val original = GroupFormQuestion("a", "Имя", "shortText", true, emptyList())
        val result = duplicateFormQuestion(listOf(original), "a", "b")
        assertEquals(listOf("a", "b"), result.map { it.questionId })
        assertEquals(original.title, result[1].title)
        assertEquals(listOf(original), duplicateFormQuestion(listOf(original), "missing", "b"))
    }
    @Test fun duplication_respects_thirty_question_limit_and_unique_ids() {
        val rows = (1..30).map { GroupFormQuestion("$it", "Вопрос", "shortText", true, emptyList()) }
        assertEquals(rows, duplicateFormQuestion(rows, "1", "31"))
        assertEquals(rows.take(2), duplicateFormQuestion(rows.take(2), "1", "2"))
    }
    @Test fun answer_comparison_ignores_order_and_blank_optional_entries() {
        assertTrue(sameFormAnswers(listOf(GroupFormAnswer("a", "Да"), GroupFormAnswer("b", "")), listOf(GroupFormAnswer("a", "Да"))))
        assertTrue(sameFormAnswers(listOf(GroupFormAnswer("a", null, listOf("1", "2"))), listOf(GroupFormAnswer("a", null, listOf("2", "1")))))
        assertFalse(sameFormAnswers(listOf(GroupFormAnswer("a", "Да")), listOf(GroupFormAnswer("a", "Нет"))))
    }
    @Test fun ballot_guidance_points_to_empty_and_duplicate_options() {
        assertEquals(BallotEditorProblem.Question, ballotEditorProblem(" ", listOf("Да", "Нет")))
        assertEquals(BallotEditorProblem.EmptyOption, ballotEditorProblem("Вопрос", listOf("Да", " ")))
        assertEquals(BallotEditorProblem.DuplicateOption, ballotEditorProblem("Вопрос", listOf("Да", " Да ")))
        assertNull(ballotEditorProblem("Вопрос", listOf("Да", "Нет")))
    }
    @Test fun moving_ballot_option_keeps_all_values_and_stops_at_edges() {
        assertEquals(listOf("Б", "А", "В"), moveBallotOption(listOf("А", "Б", "В"), 1, -1))
        assertEquals(listOf("А", "Б"), moveBallotOption(listOf("А", "Б"), 0, -1))
    }
}
