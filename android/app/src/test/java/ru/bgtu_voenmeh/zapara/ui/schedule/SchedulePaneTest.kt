package ru.bgtu_voenmeh.zapara.ui.schedule

import org.junit.Assert.assertEquals
import org.junit.Test
import java.time.LocalDate

class SchedulePaneTest {
    private val today = LocalDate.of(2026, 9, 8)

    @Test fun first_launch_failure_is_load_fail_not_no_group() {
        val failed = ScheduleUiState(loaded = true, hasGroup = false, today = today, selected = today, error = "empty db")
        assertEquals(ScheduleComposer.SchedulePane.LoadFail, ScheduleComposer.pane(failed))
    }

    @Test fun no_group_without_error() {
        val empty = ScheduleUiState(loaded = true, hasGroup = false, today = today, selected = today)
        assertEquals(ScheduleComposer.SchedulePane.NoGroup, ScheduleComposer.pane(empty))
    }

    @Test fun cache_keeps_day_when_error_after_pages() {
        val page = DayPage(today, true, "c", emptyList(), null, false)
        val cached = ScheduleUiState(
            loaded = true, hasGroup = true, today = today, selected = today,
            pages = mapOf(today to page), error = "network"
        )
        assertEquals(ScheduleComposer.SchedulePane.Day, ScheduleComposer.pane(cached))
    }
}
