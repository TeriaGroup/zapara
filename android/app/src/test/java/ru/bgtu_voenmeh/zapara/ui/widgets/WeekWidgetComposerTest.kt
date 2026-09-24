package ru.bgtu_voenmeh.zapara.ui.widgets

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.data.Subgroups
import java.time.LocalDate

class WeekWidgetComposerTest {
    private val identity = WidgetJobIdentity("guest", "week-test", 2)
    private val periodStart = LocalDate.of(2026, 9, 1)
    private val settings = ScheduleRepository.SettingsState(myGroupId = "g", periodStart = periodStart)

    private fun lesson(
        group: String = "g",
        day: Int = 3,
        parity: Int = 0,
        start: String = "09:00"
    ) = Lesson(groupId = group, dayOfWeek = day, parity = parity, timeStart = start)

    @Test fun wednesday_week_starts_monday_counts_unique_slots_and_filters_parity() {
        val today = LocalDate.of(2026, 9, 23) // even week
        val alreadySubgroupFiltered = listOf(
            lesson(parity = 2, start = "09:00"),
            lesson(parity = 2, start = "09:00"),
            lesson(parity = 0, start = "10:50"),
            lesson(parity = 1, start = "12:40"),
            lesson(group = "another-group", parity = 0, start = "14:55")
        )

        val snapshot = WeekWidgetComposer.fromSchedule(
            identity, settings, alreadySubgroupFiltered, today, "Группа", WidgetCopy, false
        )

        assertEquals(today.minusDays(2), snapshot.days.first().date)
        assertEquals(today.plusDays(4), snapshot.days.last().date)
        assertEquals(listOf("Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс"), snapshot.days.map { it.shortName })
        assertEquals(listOf(0, 0, 2, 0, 0, 0, 0), snapshot.days.map { it.lessonCount })
        assertEquals(listOf(today), snapshot.days.filter { it.isToday }.map { it.date })
        assertNull(snapshot.empty)
    }

    @Test fun monday_midnight_starts_the_new_calendar_week() {
        val monday = LocalDate.of(2026, 9, 28)
        val sundaySnapshot = WeekWidgetComposer.fromSchedule(
            identity, settings, emptyList(), monday.minusDays(1), "Группа", WidgetCopy, false
        )
        val mondaySnapshot = WeekWidgetComposer.fromSchedule(
            identity, settings, emptyList(), monday, "Группа", WidgetCopy, false
        )

        assertEquals(monday.minusDays(7), sundaySnapshot.days.first().date)
        assertEquals(monday.minusDays(1), sundaySnapshot.days.last().date)
        assertEquals(monday, mondaySnapshot.days.first().date)
        assertEquals(monday.plusDays(6), mondaySnapshot.days.last().date)
        assertEquals(monday.minusDays(1), sundaySnapshot.days.single { it.isToday }.date)
        assertEquals(monday, mondaySnapshot.days.single { it.isToday }.date)
    }

    @Test fun odd_week_and_parity_inversion_select_different_slots() {
        val today = LocalDate.of(2026, 9, 16) // third week from the period start is odd
        val rows = listOf(
            lesson(parity = 1, start = "09:00"),
            lesson(parity = 1, start = "10:50"),
            lesson(parity = 2, start = "12:40"),
            lesson(parity = 0, start = "14:55")
        )

        val odd = WeekWidgetComposer.fromSchedule(identity, settings, rows, today, "Группа", WidgetCopy, false)
        val inverted = WeekWidgetComposer.fromSchedule(
            identity, settings.copy(parityInvert = true), rows, today, "Группа", WidgetCopy, false
        )

        assertEquals(3, odd.days.single { it.isToday }.lessonCount)
        assertEquals(2, inverted.days.single { it.isToday }.lessonCount)
    }

    @Test fun composer_receives_the_selected_subgroup_filter_result() {
        val today = LocalDate.of(2026, 9, 21)
        val history = lesson(day = 1, start = "09:00").copy(
            subjectRaw = "лек ИСТОРИЯ", subjectNormalized = "история",
            teacherRaw = "Иванов И.И.", classroomRaw = "101;"
        )
        val sport = lesson(day = 1, start = "09:00").copy(
            index = 2, subjectRaw = "пр ФК", subjectNormalized = "фк",
            teacherRaw = "Петров П.П.", classroomRaw = "202;"
        )
        val competingSubgroups = listOf(history, sport)
        val stream = Subgroups.index(competingSubgroups).streams.single()
        val choice = mapOf(stream.id to stream.options.single { it.label.contains("ФК") }.id)
        val selectedLessons = Subgroups.visible(competingSubgroups, choice)

        assertEquals(listOf(sport), selectedLessons)
        val snapshot = WeekWidgetComposer.fromSchedule(
            identity, settings, selectedLessons, today, "Группа", WidgetCopy, false
        )
        assertEquals(1, snapshot.days.single { it.isToday }.lessonCount)
    }

    @Test fun no_group_has_empty_state_and_cleared_account_has_no_stale_content() {
        val today = LocalDate.of(2026, 9, 23)
        val noGroup = WeekWidgetComposer.fromSchedule(
            identity,
            settings.copy(myGroupId = null),
            listOf(lesson()),
            today,
            null,
            WidgetCopy,
            false
        )

        assertEquals("Выберите группу, чтобы открыть расписание", noGroup.empty)
        assertEquals(List(7) { 0 }, noGroup.days.map { it.lessonCount })
        assertTrue(noGroup.subtitle.isNotBlank())

        val cleared = WeekWidgetComposer.cleared(identity, WidgetCopy, true)
        assertTrue(cleared.cleared)
        assertTrue(cleared.isDark)
        assertEquals("", cleared.subtitle)
        assertTrue(cleared.days.isEmpty())
        assertEquals("", cleared.empty.orEmpty())
    }
}
