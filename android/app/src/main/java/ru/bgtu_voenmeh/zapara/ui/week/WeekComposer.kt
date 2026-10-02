package ru.bgtu_voenmeh.zapara.ui.week

import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import ru.bgtu_voenmeh.zapara.data.Schedule
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.UiCopy
import java.time.LocalDate
import java.util.Locale

data class WeekRowUi(val time: String, val name: String, val room: String, val type: String = "",
    val teacher: String = "", val start: String = "", val end: String = "",
    val subjectNorm: String = "", val subjectRaw: String = "",
    val teacherRaw: String = "", val classroomRaw: String = "", val typeRaw: String = "")
data class WeekDayUi(val dow: Int, val title: String, val date: LocalDate, val rows: List<WeekRowUi>, val isToday: Boolean)

object WeekBrowse {
    fun freeMinutes(before: WeekRowUi, after: WeekRowUi): Long? {
        val end = runCatching { java.time.LocalTime.parse(before.end) }.getOrNull() ?: return null
        val start = runCatching { java.time.LocalTime.parse(after.start) }.getOrNull() ?: return null
        return java.time.Duration.between(end, start).toMinutes().takeIf { it >= 15 }
    }
    fun filter(days: List<WeekDayUi>, query: String, lessonsOnly: Boolean): List<WeekDayUi> {
        val words = query.trim().lowercase(Locale.ROOT).split(Regex("\\s+")).filter(String::isNotEmpty)
        return days.mapNotNull { day ->
            val matches = if (words.isEmpty()) day.rows else day.rows.filter { row ->
                val searchable = listOf(row.name, row.teacher, row.room, row.type).joinToString(" ").lowercase(Locale.ROOT)
                words.all { word -> searchable.contains(word) }
            }
            if ((lessonsOnly || words.isNotEmpty()) && matches.isEmpty()) null else day.copy(rows = matches)
        }
    }
}

object WeekComposer {
    fun nearestDate(dow: Int, parity: Int, ctx: SchedCtx, today: LocalDate): LocalDate {
        for (i in 0 until 14) {
            val date = today.plusDays(i.toLong())
            if (date.dayOfWeek.value != dow) continue
            val odd = Parity.isOddWeek(date, ctx.periodStart, ctx.weekCount, ctx.invert)
            if (odd == (parity == 1)) return date
        }
        return today
    }

    fun compose(
        parity: Int,
        lessons: List<Lesson>,
        displayName: (norm: String, dow: Int) -> String,
        ctx: SchedCtx,
        today: LocalDate,
        copy: UiCopy,
        anchorDate: LocalDate? = null
    ): List<WeekDayUi> = (1..if (anchorDate == null) 6 else 7).map { dow ->
        val date = if (anchorDate == null) nearestDate(dow, parity, ctx, today) else {
            val monday = anchorDate.minusDays((anchorDate.dayOfWeek.value - 1).toLong())
            val week = if (Parity.isOddWeek(anchorDate, ctx.periodStart, ctx.weekCount, ctx.invert) == (parity == 1)) monday else monday.plusWeeks(1)
            week.plusDays((dow - 1).toLong())
        }
        val dayLessons = Schedule.lessonsForDate(lessons, ctx.groupId, date, ctx.periodStart, ctx.weekCount, ctx.invert)
        val rows = dayLessons.map { lesson ->
            val shown = displayName(lesson.subjectNormalized, lesson.dayOfWeek).ifBlank {
                LessonFormat.stripType(lesson.subjectRaw, lesson.typeRaw)
            }
            WeekRowUi(listOf(lesson.timeStart, lesson.timeEnd).filter(String::isNotBlank).joinToString("–"),
                shown, LessonFormat.roomLabel(lesson, copy), LessonFormat.typeLabel(lesson.typeRaw, copy),
                lesson.teacherRaw.trim(), lesson.timeStart, lesson.timeEnd, lesson.subjectNormalized,
                lesson.subjectRaw, lesson.teacherRaw, lesson.classroomRaw, lesson.typeRaw)
        }
        WeekDayUi(
            dow = dow,
            title = "${Parity.dayNumberToTitle(dow)} · ${LessonFormat.dayMonth(date)}",
            date = date,
            rows = rows,
            isToday = date == today
        )
    }
}
