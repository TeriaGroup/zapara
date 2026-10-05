package ru.bgtu_voenmeh.zapara.ui.groups

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.time.LocalDateTime
import java.time.ZoneId

class GroupFormDeadlineTest {
    @Test fun exact_form_time_roundtrips_in_selected_zone() {
        val zone = ZoneId.of("Europe/Moscow")
        val chosen = parseFormDeadline("2026-10-02", "08:30", zone)
        assertEquals(LocalDateTime.of(2026, 10, 2, 8, 30), chosen?.atZone(zone)?.toLocalDateTime())
        assertNull(parseFormDeadline("", "08:30", zone))
    }
}
