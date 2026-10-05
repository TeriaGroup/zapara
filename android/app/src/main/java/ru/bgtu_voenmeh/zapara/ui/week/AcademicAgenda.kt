package ru.bgtu_voenmeh.zapara.ui.week

import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import ru.bgtu_voenmeh.zapara.data.Schedule
import java.time.LocalDate
import java.time.LocalTime
import java.time.format.DateTimeFormatter
import ru.bgtu_voenmeh.zapara.data.isAssessment

data class AgendaLesson(val date: LocalDate, val lesson: Lesson)

internal fun agendaTime(value: String): Int = runCatching {
    LocalTime.parse(value, DateTimeFormatter.ofPattern("H:mm")).toSecondOfDay()
}.getOrDefault(Int.MAX_VALUE)

internal fun academicLessonKey(index: Int, start: String, end: String, subject: String,
    teacher: String, room: String, type: String = ""): String = listOf(index.toString(), start, end, subject, teacher.trim().ifBlank { "—" }, room, type)
    .joinToString("") { "${it.length}:$it" }
internal fun academicLessonKey(row: Lesson): String = academicLessonKey(row.index, row.timeStart,
    row.timeEnd, row.subjectRaw, row.teacherRaw, row.classroomRaw, row.typeRaw)

/** This is a projection of the saved timetable, never an independently promised exam schedule. */
internal fun assessmentAgenda(lessons: List<Lesson>, ctx: SchedCtx, from: LocalDate): List<AgendaLesson> =
    (0L..27L).flatMap { offset ->
        val day = from.plusDays(offset)
        if (day.isBefore(ctx.periodStart)) emptyList() else Schedule.lessonsForDate(lessons, ctx.groupId,
            day, ctx.periodStart, ctx.weekCount, ctx.invert).filter { lesson ->
            isAssessment(lesson)
        }.map { AgendaLesson(day, it) }
    }.sortedWith(compareBy<AgendaLesson> { it.date }.thenBy { agendaTime(it.lesson.timeStart) })
        .distinctBy { academicLessonKey(it.lesson) + ":" + it.lesson.typeRaw + ":" + it.lesson.parity + ":" + it.lesson.dayOfWeek }
