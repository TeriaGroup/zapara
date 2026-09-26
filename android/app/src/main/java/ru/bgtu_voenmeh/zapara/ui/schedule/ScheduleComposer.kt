package ru.bgtu_voenmeh.zapara.ui.schedule

import ru.bgtu_voenmeh.zapara.data.Homework
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import ru.bgtu_voenmeh.zapara.data.Schedule
import ru.bgtu_voenmeh.zapara.data.Subgroups
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

    fun resetsPager(event: ru.bgtu_voenmeh.zapara.ui.AppEvent): Boolean =
        event is ru.bgtu_voenmeh.zapara.ui.AppEvent.GroupChanged ||
            event is ru.bgtu_voenmeh.zapara.ui.AppEvent.ScheduleChanged

    fun pageIndex(date: LocalDate, today: LocalDate): Int =
        (ChronoUnit.DAYS.between(today, date).toInt() + TODAY_INDEX).coerceIn(0, PAGE_COUNT - 1)

    fun dateAt(index: Int, today: LocalDate): LocalDate =
        today.plusDays((index - TODAY_INDEX).toLong())

    /** The selected day is absolute; midnight only changes relative labels. */
    fun syncToday(clockToday: LocalDate, stateToday: LocalDate, selected: LocalDate): Pair<LocalDate, LocalDate> = clockToday to selected

    data class Break(val start: LocalTime, val end: LocalTime) {
        val minutes: Long get() = ChronoUnit.MINUTES.between(start, end)
    }

    fun breaks(rows: List<LessonUi>): List<Break> {
        val intervals = rows.mapNotNull { row -> runCatching { LocalTime.parse(row.timeStart) to LocalTime.parse(row.timeEnd) }.getOrNull() }
            .filter { it.second > it.first }.sortedBy { it.first }
        val merged = mutableListOf<Pair<LocalTime, LocalTime>>()
        intervals.forEach { interval ->
            val previous = merged.lastOrNull()
            if (previous != null && interval.first <= previous.second) merged[merged.lastIndex] = previous.first to maxOf(previous.second, interval.second)
            else merged.add(interval)
        }
        return merged.zipWithNext().map { Break(it.first.second, it.second.first) }.filter { it.minutes >= 30 }
    }

    fun conflicts(rows: List<LessonUi>): Set<LessonUi> = rows.filter { first ->
        rows.any { second -> first !== second && runCatching {
            LocalTime.parse(first.timeStart) < LocalTime.parse(second.timeEnd) && LocalTime.parse(second.timeStart) < LocalTime.parse(first.timeEnd)
        }.getOrDefault(false) }
    }.toSet()

    fun deadlines(date: LocalDate, subjects: Set<String>, homework: List<Homework>): List<Homework> = homework.filter {
        it.due?.let { due -> due >= date && due <= date.plusDays(2) } ?: (it.norm in subjects)
    }.sortedWith(compareBy<Homework> { it.due ?: LocalDate.MAX }.thenBy { it.id })

    fun millisUntilNextMinute(now: LocalDateTime): Long = ChronoUnit.MILLIS.between(now,now.withSecond(0).withNano(0).plusMinutes(1)).coerceIn(1L,60_000L)

    fun atClock(page: DayPage, now: LocalDateTime): DayPage {
        val today = page.date == now.toLocalDate()
        return page.copy(isToday = today, lessons = page.lessons.map { row ->
            row.copy(isPast = today && runCatching { LocalTime.parse(row.timeEnd) <= now.toLocalTime() }.getOrDefault(false))
        })
    }

    fun purgeShared(state: ScheduleUiState): ScheduleUiState = state.copy(
        pages = state.pages.mapValues { (_, page) -> page.copy(deadlines = page.deadlines.filter { it.sharedId == null }) },
        subjectRows = state.subjectRows.filter { it.sharedId == null }, sharedDetail = null, undoShared = null)

    fun featured(page: DayPage, now: LocalDateTime): LessonUi? = when {
        page.date > now.toLocalDate() -> page.lessons.firstOrNull()
        page.date < now.toLocalDate() -> null
        else -> page.lessons.firstOrNull { runCatching { LocalTime.parse(it.timeEnd) > now.toLocalTime() }.getOrDefault(false) }
    }

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
        copy: UiCopy,
        choices: Map<String, String> = emptyMap()
    ): DayPage {
        val isSunday = date.dayOfWeek == DayOfWeek.SUNDAY
        val odd = Parity.isOddWeek(date, ctx.periodStart, ctx.weekCount, ctx.invert)
        val weekNumber = Parity.weekNumber(date, ctx.periodStart)
        val caption = LessonFormat.caption(date, odd, weekNumber, copy)
        val subgroupIndex = Subgroups.index(allLessons)
        val visible = Subgroups.visible(allLessons, choices)
        val lessons = Schedule.lessonsForDate(visible, ctx.groupId, date, ctx.periodStart, ctx.weekCount, ctx.invert)
        val isToday = date == now.toLocalDate()
        val nowTime = now.toLocalTime()
        val rows = lessons.map { lesson ->
            val shown = displayName(lesson.subjectNormalized, lesson.dayOfWeek).ifBlank {
                LessonFormat.stripType(lesson.subjectRaw, lesson.typeRaw)
            }
            val original = LessonFormat.stripType(lesson.subjectRaw, lesson.typeRaw)
            val end = runCatching { LocalTime.parse(lesson.timeEnd) }.getOrNull()
            val next = Schedule.nextOccurrenceBySubject(
                visible, ctx.groupId, lesson.subjectNormalized, date,
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
                dayOfWeek = lesson.dayOfWeek,
                subgroup = Subgroups.mark(lesson, lessons, subgroupIndex, choices)?.let { mark ->
                    SubgroupMarkUi(
                        mark.streamId,
                        mark.options.map { SubgroupOptionUi(it.id, it.label) },
                        mark.chosenId,
                        mark.showChooser
                    )
                }
            )
        }
        val hint = if (!isSunday && rows.isEmpty()) nextHint(date, allLessons, ctx, displayName, copy, choices) else null
        val nextKnown = if (rows.isEmpty()) (1L..60L).map { date.plusDays(it) }.firstOrNull {
            Schedule.lessonsForDate(visible, ctx.groupId, it, ctx.periodStart, ctx.weekCount, ctx.invert).isNotEmpty()
        } else null
        return DayPage(date, isToday, caption, rows, hint, isSunday, nextKnownDate = nextKnown)
    }

    private fun nextHint(
        from: LocalDate,
        all: List<Lesson>,
        ctx: SchedCtx,
        displayName: (String, Int) -> String,
        copy: UiCopy,
        choices: Map<String, String> = emptyMap()
    ): String? {
        val subgroupIndex = Subgroups.index(all)
        for (offset in 1..60) {
            val date = from.plusDays(offset.toLong())
            if (date.dayOfWeek == DayOfWeek.SUNDAY) continue
            val day = Schedule.lessonsForDate(all, ctx.groupId, date, ctx.periodStart, ctx.weekCount, ctx.invert)
                .filter { Subgroups.keep(it, subgroupIndex, choices) }
            val lesson = day.firstOrNull() ?: continue
            val name = displayName(lesson.subjectNormalized, lesson.dayOfWeek).ifBlank {
                LessonFormat.stripType(lesson.subjectRaw, lesson.typeRaw)
            }
            return LessonFormat.nextHint(date, name, copy)
        }
        return null
    }
}
