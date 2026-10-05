package ru.bgtu_voenmeh.zapara.ui.groups

import ru.bgtu_voenmeh.zapara.data.communities.GroupFormAnswer
import ru.bgtu_voenmeh.zapara.data.communities.GroupFormQuestion

internal fun missingRequiredQuestions(questions: List<GroupFormQuestion>, answers: List<GroupFormAnswer>): List<GroupFormQuestion> =
    questions.filter { question ->
        question.required && answers.firstOrNull { it.questionId == question.questionId }
            ?.let { !it.text.isNullOrBlank() || it.choices.isNotEmpty() } != true
    }

internal fun rolePeopleShown(total: Int, requested: Int): Int = total.coerceAtLeast(0).coerceAtMost(requested.coerceAtLeast(40))
