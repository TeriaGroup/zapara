package ru.bgtu_voenmeh.zapara.ui.widgets

import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.Schedule
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.UiCopy
import java.time.Duration
import java.time.LocalDateTime
import java.time.LocalTime
import java.util.Locale

enum class TimerPhaseKind {
    Lesson,
    Break,
    Waiting,
    Finished,
    EmptyDay,
    NoGroup
}

data class TimerWidgetSnapshot(
    val identity: WidgetJobIdentity,
    val timeText: String,
    val phaseText: String,
    val subject: String,
    val detail: String,
    val kind: TimerPhaseKind,
    val fraction: Float,
    val endsAt: LocalDateTime? = null,
    val cleared: Boolean = false,
    val isDark: Boolean = false,
    val nextRefreshAt: LocalDateTime? = null
)

internal fun earlierRefresh(left: LocalDateTime?, right: LocalDateTime?): LocalDateTime? =
    listOfNotNull(left, right).minOrNull()

// The visible tick must not use setExactAndAllowWhileIdle. That quota is about
// once a minute per app, so a per-minute widget alarm was pushing the bell late
// and the launcher chronometer kept counting past zero.
internal fun timerDigitText(remainingMs: Long): String {
    if (remainingMs <= 0L) return "00:00"
    return TimerWidgetComposer.clockText((remainingMs + 999L) / 1000L)
}

internal fun timerPulseDelayMs(interactive: Boolean, exactAlarms: Boolean, untilBellMs: Long): Long? {
    if (!exactAlarms || untilBellMs <= 0L) return null
    val delay = if (interactive) 1_000L else 60_000L
    return minOf(delay, untilBellMs)
}

// While a countdown is running, wake at its end. The composer's next minute
// must not become another idle alarm: that quota was deferring the bell.
// Waiting for the first pair has no end, so the composer's refresh (the start)
// stays the wake.
internal fun widgetWakeAt(
    scheduleAt: LocalDateTime?,
    phaseEndsAt: LocalDateTime?,
    phaseRefreshAt: LocalDateTime?,
    timerPlaced: Boolean,
    timerCleared: Boolean
): LocalDateTime? {
    if (!timerPlaced || timerCleared) return scheduleAt
    return earlierRefresh(scheduleAt, phaseEndsAt ?: phaseRefreshAt)
}

object TimerWidgetComposer {
    private const val PAIR_MINUTES = 95L

    fun cleared(identity: WidgetJobIdentity, copy: UiCopy, isDark: Boolean = false) = TimerWidgetSnapshot(
        identity = identity,
        timeText = "",
        phaseText = "",
        subject = "",
        detail = "",
        kind = TimerPhaseKind.EmptyDay,
        fraction = 0f,
        cleared = true,
        isDark = isDark
    )

    fun fromTimer(
        identity: WidgetJobIdentity,
        settings: ScheduleRepository.SettingsState,
        allLessons: List<Lesson>,
        now: LocalDateTime,
        displayName: (Lesson) -> String,
        copy: UiCopy,
        cleared: Boolean = false,
        isDark: Boolean = false
    ): TimerWidgetSnapshot {
        if (cleared) return cleared(identity, copy, isDark)
        val gid = settings.myGroupId.orEmpty()
        if (gid.isEmpty()) {
            return quiet(identity, copy.get("empty_no_group"), TimerPhaseKind.NoGroup, isDark, null)
        }
        val today = now.toLocalDate()
        val lessons = Schedule.lessonsForDate(
            allLessons, gid, today, settings.periodStart, settings.weekCount, settings.parityInvert
        )
        val spans = lessons.mapNotNull { span(it) }
        if (spans.isEmpty()) {
            return quiet(
                identity,
                copy.get("no_lessons_day"),
                TimerPhaseKind.EmptyDay,
                isDark,
                today.plusDays(1).atStartOfDay()
            )
        }
        val clock = now.toLocalTime()
        val ongoing = spans.filter { !it.start.isAfter(clock) && it.end.isAfter(clock) }
        if (ongoing.isNotEmpty()) {
            val holder = ongoing.maxWith(compareBy({ it.end }, { -it.lesson.index }))
            return active(
                identity = identity,
                kind = TimerPhaseKind.Lesson,
                phase = copy.get("widget_timer_lesson"),
                subject = shown(holder.lesson, displayName),
                detail = room(holder.lesson, copy),
                start = today.atTime(holder.start),
                end = today.atTime(holder.end),
                now = now,
                isDark = isDark
            )
        }
        val next = spans.filter { it.start.isAfter(clock) }.minWithOrNull(compareBy({ it.start }, { it.lesson.index }))
        val previousEnd = spans.filter { !it.end.isAfter(clock) }.maxOfOrNull { it.end }
        if (next != null && previousEnd != null) {
            return active(
                identity = identity,
                kind = TimerPhaseKind.Break,
                phase = copy.get("widget_timer_break"),
                subject = shown(next.lesson, displayName),
                detail = copy.get("widget_timer_at", next.lesson.timeStart),
                start = today.atTime(previousEnd),
                end = today.atTime(next.start),
                now = now,
                isDark = isDark
            )
        }
        if (next != null) {
            return quiet(
                identity,
                copy.get("widget_timer_idle"),
                TimerPhaseKind.Waiting,
                isDark,
                today.atTime(next.start),
                subject = copy.get("widget_timer_next", shown(next.lesson, displayName), next.lesson.timeStart)
            )
        }
        return quiet(
            identity,
            copy.get("widget_timer_done"),
            TimerPhaseKind.Finished,
            isDark,
            today.plusDays(1).atStartOfDay()
        )
    }

    internal fun clockText(secondsLeft: Long): String {
        if (secondsLeft <= 0) return ""
        val hours = secondsLeft / 3600
        val minutes = (secondsLeft % 3600) / 60
        val seconds = secondsLeft % 60
        return if (hours > 0) {
            String.format(Locale.ROOT, "%d:%02d:%02d", hours, minutes, seconds)
        } else {
            String.format(Locale.ROOT, "%02d:%02d", minutes, seconds)
        }
    }

    private fun active(
        identity: WidgetJobIdentity,
        kind: TimerPhaseKind,
        phase: String,
        subject: String,
        detail: String,
        start: LocalDateTime,
        end: LocalDateTime,
        now: LocalDateTime,
        isDark: Boolean
    ): TimerWidgetSnapshot {
        val secondsLeft = Duration.between(now, end).seconds.coerceAtLeast(0)
        val total = Duration.between(start, end).seconds
        val fraction = if (total <= 0 || secondsLeft <= 0) 0f else (secondsLeft.toFloat() / total.toFloat()).coerceIn(0f, 1f)
        val nextMinute = now.withSecond(0).withNano(0).plusMinutes(1)
        val refresh = if (!end.isAfter(nextMinute)) end else nextMinute
        return TimerWidgetSnapshot(
            identity = identity,
            timeText = clockText(secondsLeft),
            phaseText = phase,
            subject = subject,
            detail = detail,
            kind = kind,
            fraction = fraction,
            endsAt = end,
            isDark = isDark,
            nextRefreshAt = if (secondsLeft <= 0) null else refresh
        )
    }

    private data class Span(val lesson: Lesson, val start: LocalTime, val end: LocalTime)

    private fun span(lesson: Lesson): Span? {
        val start = runCatching { LocalTime.parse(lesson.timeStart) }.getOrNull() ?: return null
        val parsedEnd = runCatching { LocalTime.parse(lesson.timeEnd) }.getOrNull()
        val end = if (parsedEnd != null && parsedEnd.isAfter(start)) parsedEnd else start.plusMinutes(PAIR_MINUTES)
        if (!end.isAfter(start)) return null
        return Span(lesson, start, end)
    }

    private fun shown(lesson: Lesson, displayName: (Lesson) -> String): String =
        displayName(lesson).ifBlank { LessonFormat.stripType(lesson.subjectRaw, lesson.typeRaw) }

    private fun room(lesson: Lesson, copy: UiCopy): String {
        if (lesson.roomRaw.isBlank() && lesson.classroomRaw.isBlank()) return ""
        return LessonFormat.roomLabel(lesson, copy)
    }

    private fun quiet(
        identity: WidgetJobIdentity,
        phase: String,
        kind: TimerPhaseKind,
        isDark: Boolean,
        nextRefreshAt: LocalDateTime?,
        subject: String = ""
    ) = TimerWidgetSnapshot(
        identity = identity,
        timeText = "",
        phaseText = phase,
        subject = subject,
        detail = "",
        kind = kind,
        fraction = 0f,
        isDark = isDark,
        nextRefreshAt = nextRefreshAt
    )
}
