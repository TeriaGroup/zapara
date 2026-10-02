package ru.bgtu_voenmeh.zapara.ui.homework

import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.Schedule
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime

internal data class HomeworkLessonTarget(val date: LocalDate, val time: String, val subjectNorm: String)

internal fun nextHomeworkLesson(lessons: List<Lesson>, context: SchedCtx, subject: String,
    now: LocalDateTime): HomeworkLessonTarget? {
    val norm = Parity.normalizeSubject(subject)
    if (norm.isBlank() || context.groupId.isBlank()) return null
    for (offset in 0L..28L) {
        val day = now.toLocalDate().plusDays(offset)
        val match = Schedule.lessonsForDate(lessons, context.groupId, day,
            context.periodStart, context.weekCount, context.invert)
            .asSequence()
            .filter { Parity.sameSubject(it.subjectNormalized, norm) }
            .filter { offset > 0 || runCatching { LocalTime.parse(it.timeEnd) }
                .getOrNull()?.isAfter(now.toLocalTime()) == true }
            .minByOrNull { it.timeStart }
        if (match != null) return HomeworkLessonTarget(day, match.timeStart, match.subjectNormalized)
    }
    return null
}
