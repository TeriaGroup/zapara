package ru.bgtu_voenmeh.zapara.ui.week

import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import ru.bgtu_voenmeh.zapara.data.Schedule
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.UiCopy
import java.time.LocalDate

data class WeekRowUi(val time: String, val name: String, val room: String)
data class WeekDayUi(val dow: Int, val title: String, val date: LocalDate, val rows: List<WeekRowUi>, val isToday: Boolean)

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
        copy: UiCopy
    ): List<WeekDayUi> = (1..6).map { dow ->
        val date = nearestDate(dow, parity, ctx, today)
        val dayLessons = Schedule.lessonsForDate(lessons, ctx.groupId, date, ctx.periodStart, ctx.weekCount, ctx.invert)
        val rows = dayLessons.map { lesson ->
            val shown = displayName(lesson.subjectNormalized, lesson.dayOfWeek).ifBlank {
                LessonFormat.stripType(lesson.subjectRaw, lesson.typeRaw)
            }
            WeekRowUi(lesson.timeStart, shown, LessonFormat.roomLabel(lesson, copy))
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
