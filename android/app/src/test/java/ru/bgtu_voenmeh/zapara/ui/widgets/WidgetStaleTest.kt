package ru.bgtu_voenmeh.zapara.ui.widgets

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.time.LocalDate

class WidgetStaleTest {
    @Test fun age_uses_moscow_calendar_day_and_does_not_claim_unknown_age() {
        assertEquals(2, widgetStaleDays("2026-10-01T22:30:00Z", LocalDate.of(2026, 10, 4)))
        assertNull(widgetStaleDays("2026-10-01T22:30:00Z", LocalDate.of(2026, 10, 3)))
        assertNull(widgetStaleDays(null, LocalDate.of(2026, 10, 4)))
        assertNull(widgetStaleDays("bad", LocalDate.of(2026, 10, 4)))
    }
}
