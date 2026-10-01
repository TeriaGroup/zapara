package ru.bgtu_voenmeh.zapara.ui.groups

import ru.bgtu_voenmeh.zapara.data.communities.GroupFormAnswer
import ru.bgtu_voenmeh.zapara.data.communities.GroupFormQuestion

internal fun duplicateFormQuestion(rows: List<GroupFormQuestion>, id: String, newId: String): List<GroupFormQuestion> {
    val index = rows.indexOfFirst { it.questionId == id }
    if (index < 0 || rows.size >= 30 || rows.any { it.questionId == newId }) return rows
    return rows.toMutableList().apply { add(index + 1, rows[index].copy(questionId = newId)) }
}

internal fun sameFormAnswers(left: List<GroupFormAnswer>, right: List<GroupFormAnswer>): Boolean {
    fun canonical(rows: List<GroupFormAnswer>) = rows.filter { !it.text.isNullOrEmpty() || it.choices.isNotEmpty() }
        .associate { it.questionId to (it.text.orEmpty() to it.choices.toSet()) }
    return canonical(left) == canonical(right)
}

internal enum class BallotEditorProblem { Question, EmptyOption, DuplicateOption }
internal fun ballotEditorProblem(question: String, options: List<String>): BallotEditorProblem? = when {
    question.isBlank() -> BallotEditorProblem.Question
    options.size !in 2..6 || options.any(String::isBlank) -> BallotEditorProblem.EmptyOption
    options.map(String::trim).distinct().size != options.size -> BallotEditorProblem.DuplicateOption
    else -> null
}

internal fun moveBallotOption(rows: List<String>, index: Int, delta: Int): List<String> {
    val target = index + delta
    if (index !in rows.indices || target !in rows.indices) return rows
    return rows.toMutableList().apply { add(target, removeAt(index)) }
}
