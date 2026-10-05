package ru.bgtu_voenmeh.zapara.ui.widgets

import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.Schedule
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.ui.UiCopy
import java.time.DayOfWeek
import java.time.LocalDate

data class WeekWidgetDay(
    val date: LocalDate,
    val shortName: String,
    val lessonCount: Int,
    val isToday: Boolean,
    val timeSpan: String = ""
)

data class WeekWidgetSnapshot(
    val identity: WidgetJobIdentity,
    val title: String,
    val subtitle: String,
    val days: List<WeekWidgetDay>,
    val empty: String?,
    val cleared: Boolean = false,
    val isDark: Boolean = false,
    val readError: String? = null
)

object WeekWidgetComposer {
    private val dayNameKeys = listOf(
        "widget_week_mon", "widget_week_tue", "widget_week_wed", "widget_week_thu",
        "widget_week_fri", "widget_week_sat", "widget_week_sun"
    )

    fun cleared(identity: WidgetJobIdentity, copy: UiCopy, isDark: Boolean = false): WeekWidgetSnapshot {
        return WeekWidgetSnapshot(
            identity = identity,
            title = copy.get("widget_week_title"),
            subtitle = "",
            days = emptyList(),
            empty = copy.get("widget_loading"),
            cleared = true,
            isDark = isDark
        )
    }

    fun fromSchedule(
        identity: WidgetJobIdentity,
        settings: ScheduleRepository.SettingsState,
        allLessons: List<Lesson>,
        today: LocalDate,
        groupName: String?,
        copy: UiCopy,
        isDark: Boolean
    ): WeekWidgetSnapshot {
        val title = copy.get("widget_week_title")
        val groupId = settings.myGroupId.orEmpty()
        val subtitle = widgetSubtitle(identity, groupName, copy)
        val monday = today.with(DayOfWeek.MONDAY)
        if (groupId.isEmpty()) {
            return WeekWidgetSnapshot(
                identity = identity,
                title = title,
                subtitle = subtitle,
                days = emptyList(),
                empty = copy.get("widget_week_no_group"),
                isDark = isDark
            )
        }

        val daily = (0L..6L).associate { offset ->
            val date = monday.plusDays(offset)
            val dayLessons = Schedule.lessonsForDate(
                allLessons,
                groupId,
                date,
                settings.periodStart,
                settings.weekCount,
                settings.parityInvert
            )
            date to dayLessons
        }
        val counts = daily.mapValues { (_, rows) -> rows.map(Lesson::timeStart).filter(String::isNotBlank).distinct().size }
        return WeekWidgetSnapshot(
            identity = identity,
            title = title,
            subtitle = subtitle,
            days = days(monday, counts, today, copy).map { day ->
                day.copy(timeSpan = validWidgetDaySpan(daily[day.date].orEmpty()))
            },
            empty = null,
            isDark = isDark
        )
    }

    private fun days(
        monday: LocalDate,
        counts: Map<LocalDate, Int>,
        today: LocalDate?,
        copy: UiCopy
    ) = (0L..6L).map { offset ->
        val date = monday.plusDays(offset)
        WeekWidgetDay(
            date = date,
            shortName = copy.get(dayNameKeys[offset.toInt()]),
            lessonCount = counts[date] ?: 0,
            isToday = date == today
        )
    }
}

internal fun validWidgetDaySpan(rows: List<Lesson>): String {
    val valid = rows.mapNotNull { row ->
        val start = runCatching { java.time.LocalTime.parse(row.timeStart) }.getOrNull()
        val end = runCatching { java.time.LocalTime.parse(row.timeEnd) }.getOrNull()
        if (start == null || end == null || !end.isAfter(start)) null else start to end
    }
    if (valid.isEmpty()) return ""
    return "${valid.minOf { it.first }}–${valid.maxOf { it.second }}"
}
