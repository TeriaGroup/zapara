package ru.bgtu_voenmeh.zapara.ui.schedule

import ru.bgtu_voenmeh.zapara.data.Homework
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import ru.bgtu_voenmeh.zapara.data.Schedule
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.UiCopy
import java.time.DayOfWeek
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.temporal.ChronoUnit

object ScheduleComposer {
    const val PAGE_COUNT = 731
    const val TODAY_INDEX = 365

    fun pageIndex(date: LocalDate, today: LocalDate): Int =
        (ChronoUnit.DAYS.between(today, date).toInt() + TODAY_INDEX).coerceIn(0, PAGE_COUNT - 1)

    fun dateAt(index: Int, today: LocalDate): LocalDate =
        today.plusDays((index - TODAY_INDEX).toLong())

    enum class SchedulePane { Loading, NoGroup, LoadFail, Day }

    fun pane(state: ScheduleUiState): SchedulePane = when {
        !state.loaded && state.pages.isEmpty() && state.error == null -> SchedulePane.Loading
        state.error != null && state.pages.isEmpty() -> SchedulePane.LoadFail
        !state.hasGroup -> SchedulePane.NoGroup
        else -> SchedulePane.Day
    }

    fun page(
        date: LocalDate,
        allLessons: List<Lesson>,
        ctx: SchedCtx,
        now: LocalDateTime,
        displayName: (norm: String, dow: Int) -> String,
        homeworkFor: (norm: String) -> List<Homework>,
        friendsFor: (Lesson) -> List<FriendDotUi>,
        copy: UiCopy
    ): DayPage {
        val isSunday = date.dayOfWeek == DayOfWeek.SUNDAY
        val odd = Parity.isOddWeek(date, ctx.periodStart, ctx.weekCount, ctx.invert)
        val weekNumber = Parity.weekNumber(date, ctx.periodStart)
        val caption = LessonFormat.caption(date, odd, weekNumber, copy)
        val lessons = Schedule.lessonsForDate(allLessons, ctx.groupId, date, ctx.periodStart, ctx.weekCount, ctx.invert)
        val isToday = date == now.toLocalDate()
        val nowTime = now.toLocalTime()
        val rows = lessons.map { lesson ->
            val shown = displayName(lesson.subjectNormalized, lesson.dayOfWeek).ifBlank {
                LessonFormat.stripType(lesson.subjectRaw, lesson.typeRaw)
            }
            val original = LessonFormat.stripType(lesson.subjectRaw, lesson.typeRaw)
            val end = runCatching { LocalTime.parse(lesson.timeEnd) }.getOrNull()
            val next = Schedule.nextOccurrenceBySubject(
                allLessons, ctx.groupId, lesson.subjectNormalized, date,
                ctx.periodStart, ctx.weekCount, ctx.invert
            )
            LessonUi(
                index = lesson.index,
                timeStart = lesson.timeStart,
                timeEnd = lesson.timeEnd,
                type = LessonFormat.typeLabel(lesson.typeRaw, copy),
                name = shown,
                original = original.takeIf { it != shown },
                teacher = lesson.teacherRaw.ifBlank { "—" },
                room = LessonFormat.roomLabel(lesson, copy),
                classroomRaw = lesson.classroomRaw,
                nextDate = next?.let { LessonFormat.dayMonth(it) },
                homework = homeworkFor(lesson.subjectNormalized).map { hw ->
                    HomeworkRowUi(hw.id, hw.text, LessonFormat.hwCardLabel(hw, copy), hw.status, hw.done)
                },
                friends = friendsFor(lesson),
                isPast = isToday && end != null && end.isBefore(nowTime),
                subjectRaw = lesson.subjectRaw,
                subjectNorm = lesson.subjectNormalized,
                remote = LessonFormat.isRemote(lesson.classroomRaw),
                dayOfWeek = lesson.dayOfWeek
            )
        }
        val hint = if (!isSunday && rows.isEmpty()) nextHint(date, allLessons, ctx, displayName, copy) else null
        return DayPage(date, isToday, caption, rows, hint, isSunday)
    }

    private fun nextHint(
        from: LocalDate,
        all: List<Lesson>,
        ctx: SchedCtx,
        displayName: (String, Int) -> String,
        copy: UiCopy
    ): String? {
        for (offset in 1..60) {
            val date = from.plusDays(offset.toLong())
            if (date.dayOfWeek == DayOfWeek.SUNDAY) continue
            val day = Schedule.lessonsForDate(all, ctx.groupId, date, ctx.periodStart, ctx.weekCount, ctx.invert)
            val lesson = day.firstOrNull() ?: continue
            val name = displayName(lesson.subjectNormalized, lesson.dayOfWeek).ifBlank {
                LessonFormat.stripType(lesson.subjectRaw, lesson.typeRaw)
            }
            return LessonFormat.nextHint(date, name, copy)
        }
        return null
    }
}
