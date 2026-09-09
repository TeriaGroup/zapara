package ru.bgtu_voenmeh.zapara.ui.widgets

import ru.bgtu_voenmeh.zapara.AppContainer
import ru.bgtu_voenmeh.zapara.data.Homework
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.Schedule
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.UiCopy
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkGroups
import ru.bgtu_voenmeh.zapara.ui.schedule.SmartStart
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime

data class ScheduleWidgetRow(
    val name: String,
    val meta: String,
    val isPast: Boolean
)

data class ScheduleWidgetSnapshot(
    val identity: WidgetJobIdentity,
    val title: String,
    val subtitle: String,
    val empty: String?,
    val rows: List<ScheduleWidgetRow>,
    val cleared: Boolean = false,
    val isDark: Boolean = false
)

data class HomeworkWidgetRow(
    val subject: String,
    val detail: String,
    val tone: String
)

data class HomeworkWidgetSnapshot(
    val identity: WidgetJobIdentity,
    val title: String,
    val subtitle: String,
    val empty: String?,
    val rows: List<HomeworkWidgetRow>,
    val cleared: Boolean = false,
    val isDark: Boolean = false
)

internal fun widgetSubtitle(identity: WidgetJobIdentity, groupName: String?, copy: UiCopy): String {
    val group = groupName.orEmpty()
    return when {
        identity.isGuest && group.isNotEmpty() -> copy.get("chip_group", copy.get("widget_guest"), group)
        identity.isGuest -> copy.get("widget_guest")
        group.isNotEmpty() -> group
        else -> copy.get("empty_no_group")
    }
}

object ScheduleWidgetComposer {
    const val MAX_ROWS = 4

    fun cleared(identity: WidgetJobIdentity, copy: UiCopy, isDark: Boolean = false) = fromSchedule(
        identity = identity,
        settings = ScheduleRepository.SettingsState(),
        allLessons = emptyList(),
        now = LocalDateTime.of(2026, 1, 1, 0, 0),
        groupName = null,
        displayName = { "" },
        copy = copy,
        cleared = true,
        isDark = isDark
    )

    fun fromSchedule(
        identity: WidgetJobIdentity,
        settings: ScheduleRepository.SettingsState,
        allLessons: List<Lesson>,
        now: LocalDateTime,
        groupName: String?,
        displayName: (Lesson) -> String,
        copy: UiCopy,
        cleared: Boolean = false,
        isDark: Boolean = false
    ): ScheduleWidgetSnapshot {
        val title = copy.get("nav_schedule")
        if (cleared) {
            return ScheduleWidgetSnapshot(identity, title, "", null, emptyList(), true, isDark)
        }
        val gid = settings.myGroupId.orEmpty()
        val subtitle = widgetSubtitle(identity, groupName, copy)
        if (gid.isEmpty()) {
            return ScheduleWidgetSnapshot(identity, title, subtitle, copy.get("empty_no_group"), emptyList(), false, isDark)
        }
        val today = now.toLocalDate()
        val todayLessons = Schedule.lessonsForDate(
            allLessons, gid, today, settings.periodStart, settings.weekCount, settings.parityInvert
        )
        val date = SmartStart.initialDate(now, todayLessons)
        val lessons = if (date == today) todayLessons else Schedule.lessonsForDate(
            allLessons, gid, date, settings.periodStart, settings.weekCount, settings.parityInvert
        )
        val rows = lessons.take(MAX_ROWS).map { lesson ->
            val shown = displayName(lesson).ifBlank { LessonFormat.stripType(lesson.subjectRaw, lesson.typeRaw) }
            val room = LessonFormat.roomLabel(lesson, copy)
            val end = runCatching { LocalTime.parse(lesson.timeEnd) }.getOrNull()
            ScheduleWidgetRow(
                name = shown,
                meta = "${lesson.timeStart} – ${lesson.timeEnd} · $room",
                isPast = date == today && end != null && end.isBefore(now.toLocalTime())
            )
        }
        val empty = if (rows.isEmpty()) copy.get("no_lessons_day") else null
        return ScheduleWidgetSnapshot(identity, title, subtitle, empty, rows, false, isDark)
    }
}

object HomeworkWidgetComposer {
    const val MAX_ROWS = 4

    fun cleared(identity: WidgetJobIdentity, copy: UiCopy, isDark: Boolean = false) = fromHomework(
        identity = identity,
        settings = ScheduleRepository.SettingsState(),
        homework = emptyList(),
        lessons = emptyList(),
        today = LocalDate.of(2026, 1, 1),
        groupName = null,
        displayName = { it },
        copy = copy,
        cleared = true,
        isDark = isDark
    )

    fun fromHomework(
        identity: WidgetJobIdentity,
        settings: ScheduleRepository.SettingsState,
        homework: List<Homework>,
        lessons: List<Lesson>,
        today: LocalDate,
        groupName: String?,
        displayName: (String) -> String,
        copy: UiCopy,
        cleared: Boolean = false,
        isDark: Boolean = false
    ): HomeworkWidgetSnapshot {
        val title = copy.get("nav_homework")
        if (cleared) {
            return HomeworkWidgetSnapshot(identity, title, "", null, emptyList(), true, isDark)
        }
        val gid = settings.myGroupId.orEmpty()
        val subtitle = widgetSubtitle(identity, groupName, copy)
        if (gid.isEmpty()) {
            return HomeworkWidgetSnapshot(identity, title, subtitle, copy.get("empty_no_group"), emptyList(), false, isDark)
        }
        val rows = homework
            .filter { !it.done && it.status != "done" }
            .sortedWith(compareBy({ rank(it.status) }, { it.due ?: LocalDate.MAX }, { it.id }))
            .take(MAX_ROWS)
            .map { hw ->
                val lesson = lessons.firstOrNull { it.subjectNormalized == hw.norm }
                val subject = displayName(hw.norm).ifBlank {
                    lesson?.let { LessonFormat.stripType(it.subjectRaw, it.typeRaw) } ?: hw.norm
                }
                val due = HomeworkGroups.dueLabel(hw, today, copy)
                val detail = if (hw.text.isBlank()) due else "${hw.text} · $due"
                HomeworkWidgetRow(subject, detail, tone(hw.status))
            }
        val empty = if (rows.isEmpty()) copy.get("hw_empty_title") else null
        return HomeworkWidgetSnapshot(identity, title, subtitle, empty, rows, false, isDark)
    }

    internal fun rank(status: String): Int = when (status) {
        "overdue" -> 0
        "burning_urgent" -> 1
        "burning" -> 2
        "approaching" -> 3
        else -> 4
    }

    internal fun tone(status: String): String = when (status) {
        "overdue" -> "bad"
        "burning", "burning_urgent" -> "warn"
        "done" -> "ok"
        else -> "text2"
    }
}

object WidgetSnapshots {
    fun schedule(container: AppContainer, identity: WidgetJobIdentity, cleared: Boolean, systemNight: Boolean): ScheduleWidgetSnapshot {
        val settings = container.repo.settings()
        val dark = WidgetTheme.isDark(settings.theme, systemNight)
        if (cleared) return ScheduleWidgetComposer.cleared(identity, container.copy, dark)
        val gid = settings.myGroupId.orEmpty()
        val groupName = container.repo.groups().firstOrNull { it.id == gid }?.name
        val lessons = if (gid.isEmpty()) emptyList() else container.repo.allForGroup(gid)
        return ScheduleWidgetComposer.fromSchedule(
            identity = identity,
            settings = settings,
            allLessons = lessons,
            now = container.clock(),
            groupName = groupName,
            displayName = { lesson -> container.overrides.displayNameByNorm(lesson.subjectNormalized, lesson.dayOfWeek) },
            copy = container.copy,
            isDark = dark
        )
    }

    fun homework(container: AppContainer, identity: WidgetJobIdentity, cleared: Boolean, systemNight: Boolean): HomeworkWidgetSnapshot {
        val settings = container.repo.settings()
        val dark = WidgetTheme.isDark(settings.theme, systemNight)
        if (cleared) return HomeworkWidgetComposer.cleared(identity, container.copy, dark)
        val gid = settings.myGroupId.orEmpty()
        val groupName = container.repo.groups().firstOrNull { it.id == gid }?.name
        val lessons = if (gid.isEmpty()) emptyList() else container.repo.allForGroup(gid)
        return HomeworkWidgetComposer.fromHomework(
            identity = identity,
            settings = settings,
            homework = container.homework.all(),
            lessons = lessons,
            today = container.clock().toLocalDate(),
            groupName = groupName,
            displayName = { norm ->
                val lesson = lessons.firstOrNull { it.subjectNormalized == norm }
                if (lesson != null) container.overrides.displayNameByNorm(norm, lesson.dayOfWeek) else ""
            },
            copy = container.copy,
            isDark = dark
        )
    }
}
