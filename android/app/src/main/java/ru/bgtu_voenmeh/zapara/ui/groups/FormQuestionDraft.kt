package ru.bgtu_voenmeh.zapara.ui.groups

import ru.bgtu_voenmeh.zapara.data.communities.GroupFormQuestion

internal fun moveQuestion(rows: List<GroupFormQuestion>, index: Int, offset: Int): List<GroupFormQuestion> {
    val target = index + offset
    if (index !in rows.indices || target !in rows.indices || kotlin.math.abs(offset) != 1) return rows
    return rows.toMutableList().also { java.util.Collections.swap(it, index, target) }
}

internal fun questionHasDraft(question: GroupFormQuestion): Boolean =
    question.title.isNotBlank() || question.options.any { it.isNotBlank() }
