package ru.bgtu_voenmeh.zapara.ui.schedule

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
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

    @Test fun date_cells_wrap_two_line_metrics_instead_of_a_fixed_56dp_box() {
        val source = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/schedule/DateStrip.kt").readText()
        assertFalse(source.contains("size(44.dp, 56.dp)"))
        assertTrue(source.contains("defaultMinSize(minWidth = Zapara.space.minTouch, minHeight = Zapara.space.minTouch)"))
        assertTrue(source.contains("padding(horizontal = Zapara.space.s, vertical = Zapara.space.s)"))
    }

    @Test fun date_strip_slides_to_the_selected_day_instead_of_jumping() {
        val source = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/schedule/DateStrip.kt").readText()
        assertTrue(source.contains("animateScrollToItem"))
        assertTrue(source.contains("remember(today)"))
        assertFalse(source.contains("remember(selected)"))
        assertFalse(source.contains("scrollToItem(30)"))
        val section = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/schedule/ScheduleSection.kt").readText()
        assertTrue(section.contains("pager.animateScrollToPage"))
    }
}
