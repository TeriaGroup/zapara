package ru.bgtu_voenmeh.zapara.ui.schedule

import org.junit.Assert.assertEquals
import org.junit.Test
import java.time.LocalDate

class DateStripDatesTest {
    @Test fun distant_selection_has_past_and_future_across_year_boundary() {
        val selected = LocalDate.of(2026, 12, 31)
        assertEquals(listOf(LocalDate.of(2026, 12, 29), LocalDate.of(2026, 12, 30), selected,
            LocalDate.of(2027, 1, 1), LocalDate.of(2027, 1, 2)),
            dateStripDates(selected, 5))
    }

    @Test fun today_selection_still_shows_nearby_past_and_future() {
        val today = LocalDate.of(2026, 9, 30)
        assertEquals(listOf(LocalDate.of(2026, 9, 28), LocalDate.of(2026, 9, 29), today,
            LocalDate.of(2026, 10, 1), LocalDate.of(2026, 10, 2)), dateStripDates(today, 5))
    }
}
