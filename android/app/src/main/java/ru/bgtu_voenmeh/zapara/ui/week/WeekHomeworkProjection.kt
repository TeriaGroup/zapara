package ru.bgtu_voenmeh.zapara.ui.week

import ru.bgtu_voenmeh.zapara.data.Homework
import ru.bgtu_voenmeh.zapara.data.HomeworkDue
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import java.time.LocalDate

internal data class WeekHomeworkProjection(val deadlines: List<WeekHomeworkUi>, val undated: Int)

/** Read-only projection from the exact visible timetable snapshot used to compose this week. */
internal fun projectWeekHomework(tasks: List<Homework>, lessons: List<Lesson>, context: SchedCtx,
    dates: Set<LocalDate>, displayName: (String, Int) -> String): WeekHomeworkProjection {
    var undated = 0
    val deadlines = tasks.mapNotNull { task ->
        val due = if (task.done) task.due else HomeworkDue.date({ group, day, parity ->
            lessons.filter { it.groupId == group && it.dayOfWeek == day && (it.parity == 0 || it.parity == parity) }
        }, context, task.norm, task.createdAt, task.n)
        if (due == null && !task.done) undated++
        if (due == null || due !in dates) return@mapNotNull null
        WeekHomeworkUi(task.id, displayName(task.norm, due.dayOfWeek.value).ifBlank { task.norm },
            task.text, due, task.done)
    }.sortedWith(compareBy<WeekHomeworkUi> { it.date }.thenBy { it.done }.thenBy { it.subject })
    return WeekHomeworkProjection(deadlines, undated)
}
