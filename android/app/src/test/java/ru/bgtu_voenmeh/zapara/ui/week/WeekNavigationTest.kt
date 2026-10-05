package ru.bgtu_voenmeh.zapara.ui.week

import org.junit.Assert.assertEquals
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import java.time.LocalDate

class WeekNavigationTest {
    private val monday = LocalDate.of(2026, 9, 7)

    @Test fun selecting_other_parity_moves_to_an_actual_adjacent_week() {
        val context = SchedCtx("А863С", LocalDate.of(2026, 9, 1), 2, false)
        val chosen = WeekNavigation.chooseParity(monday, 3 - WeekNavigation.parity(monday, context), context)
        assertEquals(monday.plusWeeks(1), chosen)
        assertEquals(monday.dayOfWeek, chosen.dayOfWeek)
    }

    @Test fun inverted_calendar_still_matches_selected_week_rows() {
        val context = SchedCtx("А863С", LocalDate.of(2026, 9, 1), 2, true)
        val chosen = WeekNavigation.chooseParity(monday, WeekNavigation.parity(monday, context), context)
        assertEquals(monday, chosen)
        assertEquals(3 - WeekNavigation.parity(monday, context), WeekNavigation.parity(monday, context.copy(invert = false)))
    }

    @Test fun parity_roundtrip_restores_inspected_week_across_new_year() {
        val context = SchedCtx("А863С", LocalDate.of(2026, 9, 1), 2, false)
        val first = LocalDate.of(2026, 12, 28)
        val history = WeekParityHistory()
        history.reset(first, context)
        val other = history.choose(first, 3 - WeekNavigation.parity(first, context), context)
        assertEquals(first.plusWeeks(1), other)
        assertEquals(first, history.choose(other, WeekNavigation.parity(first, context), context))
    }
}
