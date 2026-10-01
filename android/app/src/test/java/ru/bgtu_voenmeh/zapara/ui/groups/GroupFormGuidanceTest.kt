package ru.bgtu_voenmeh.zapara.ui.groups

import org.junit.Assert.assertEquals
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.communities.GroupFormAnswer
import ru.bgtu_voenmeh.zapara.data.communities.GroupFormQuestion

class GroupFormGuidanceTest {
    @Test fun guidance_identifies_first_missing_required_answer_without_changing_answers() {
        val questions = listOf(
            GroupFormQuestion("a", "Обязательный текст", "shortText", true, emptyList()),
            GroupFormQuestion("b", "Необязательный", "shortText", false, emptyList()),
            GroupFormQuestion("c", "Выбор", "singleChoice", true, listOf("Да", "Нет")))
        val answers = listOf(GroupFormAnswer("a", "  "), GroupFormAnswer("b", "Готово"))
        assertEquals(listOf("a", "c"), missingRequiredQuestions(questions, answers).map { it.questionId })
        assertEquals(answers, answers.toList())
    }

    @Test fun role_people_can_reach_beyond_initial_forty_without_exceeding_results() {
        assertEquals(40, rolePeopleShown(93, 40))
        assertEquals(80, rolePeopleShown(93, 80))
        assertEquals(93, rolePeopleShown(93, 120))
    }
}
