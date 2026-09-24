package ru.bgtu_voenmeh.zapara.ui.widgets

import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.MapResolve
import ru.bgtu_voenmeh.zapara.data.Schedule
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.UiCopy
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime

data class WayfinderWidgetSnapshot(
    val identity: WidgetJobIdentity,
    val title: String,
    val status: String,
    val subject: String,
    val time: String,
    val room: String,
    val classroomRaw: String,
    val targetDate: LocalDate?,
    val opensMap: Boolean,
    val empty: String?,
    val cleared: Boolean = false,
    val isDark: Boolean = false,
    val nextRefreshAt: LocalDateTime? = null
)

object WayfinderWidgetComposer {
    private const val PAIR_MINUTES = 95L

    fun cleared(identity: WidgetJobIdentity, copy: UiCopy, isDark: Boolean = false) = WayfinderWidgetSnapshot(
        identity = identity,
        title = copy.get("widget_wayfinder_title"),
        status = "",
        subject = "",
        time = "",
        room = "",
        classroomRaw = "",
        targetDate = null,
        opensMap = false,
        empty = null,
        cleared = true,
        isDark = isDark
    )

    fun fromSchedule(
        identity: WidgetJobIdentity,
        settings: ScheduleRepository.SettingsState,
        allLessons: List<Lesson>,
        now: LocalDateTime,
        displayName: (Lesson) -> String,
        copy: UiCopy,
        isDark: Boolean = false
    ): WayfinderWidgetSnapshot {
        val title = copy.get("widget_wayfinder_title")
        val gid = settings.myGroupId.orEmpty()
        if (gid.isBlank()) return empty(identity, title, copy.get("empty_no_group"), isDark, null)

        val today = now.toLocalDate()
        for (offset in 0..6) {
            val date = today.plusDays(offset.toLong())
            val spans = Schedule.lessonsForDate(
                allLessons, gid, date, settings.periodStart, settings.weekCount, settings.parityInvert
            ).mapNotNull(::span)
            val current = if (offset == 0) spans
                .filter { !it.start.isAfter(now.toLocalTime()) && it.end.isAfter(now.toLocalTime()) }
                .maxWithOrNull(compareBy({ it.end }, { -it.lesson.index })) else null
            val next = if (offset == 0) spans
                .filter { it.start.isAfter(now.toLocalTime()) }
                .minWithOrNull(compareBy({ it.start }, { it.lesson.index })) else spans
                .minWithOrNull(compareBy({ it.start }, { it.lesson.index }))
            val selected = current ?: next ?: continue
            val lesson = selected.lesson
            val room = if (lesson.roomRaw.isBlank() && lesson.classroomRaw.isBlank())
                copy.get("widget_wayfinder_missing_room") else LessonFormat.roomLabel(lesson, copy)
            val map = MapResolve.resolve(lesson.classroomRaw)
            val opensMap = map != null && !map.isRemote && map.hasMap && map.roomRaw.any(Char::isDigit)
            return WayfinderWidgetSnapshot(
                identity = identity,
                title = title,
                status = copy.get(if (current != null) "widget_wayfinder_now" else "widget_wayfinder_next"),
                subject = displayName(lesson).ifBlank { LessonFormat.stripType(lesson.subjectRaw, lesson.typeRaw) },
                time = "${lesson.timeStart} – ${selected.endText}",
                room = room,
                classroomRaw = lesson.classroomRaw,
                targetDate = date,
                opensMap = opensMap,
                empty = null,
                isDark = isDark,
                // A later overlapping start can replace the current holder before its end.
                // Other lessons ending cannot replace a still-active greatest-end holder.
                nextRefreshAt = date.atTime(if (current != null)
                    minOf(selected.end, spans.asSequence().map { it.start }
                        .filter { it.isAfter(now.toLocalTime()) }.minOrNull() ?: selected.end)
                    else selected.start)
            )
        }
        return empty(identity, title, copy.get("widget_wayfinder_empty"), isDark, today.plusDays(1).atStartOfDay())
    }

    private fun empty(
        identity: WidgetJobIdentity,
        title: String,
        message: String,
        isDark: Boolean,
        nextRefreshAt: LocalDateTime?
    ) = WayfinderWidgetSnapshot(
        identity, title, "", "", "", "", "", null, false, message,
        isDark = isDark, nextRefreshAt = nextRefreshAt
    )

    private data class Span(val lesson: Lesson, val start: LocalTime, val end: LocalTime, val endText: String)

    private fun span(lesson: Lesson): Span? {
        val start = runCatching { LocalTime.parse(lesson.timeStart) }.getOrNull() ?: return null
        val parsedEnd = runCatching { LocalTime.parse(lesson.timeEnd) }.getOrNull()
        val end = parsedEnd?.takeIf { it.isAfter(start) } ?: start.plusMinutes(PAIR_MINUTES)
        if (!end.isAfter(start)) return null
        return Span(lesson, start, end, parsedEnd?.takeIf { it.isAfter(start) }?.toString() ?: end.toString())
    }
}
