package ru.bgtu_voenmeh.zapara.ui.summary

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import java.time.LocalDate

class SummaryDayNavigationTest {
    private val today = LocalDate.of(2026, 9, 8)

    @Test fun day_link_resolves_absolute_date_for_filtered_parity_and_inversion() {
        listOf(false, true).forEach { invert ->
            val context = SchedCtx("А863С", LocalDate.of(2026, 9, 1), 2, invert)
            val date = SummaryDayNavigation.date(1, 0, today, context)!!
            assertEquals(1, date.dayOfWeek.value)
            assertEquals(true, Parity.isOddWeek(date, context.periodStart, context.weekCount, context.invert))
        }
    }

    @Test fun both_filter_uses_nearest_date_and_invalid_day_has_no_target() {
        val context = SchedCtx("А863С", LocalDate.of(2026, 9, 1), 2, false)
        assertEquals(today, SummaryDayNavigation.date(2, 2, today, context))
        assertNull(SummaryDayNavigation.date(8, 2, today, context))
    }
}
