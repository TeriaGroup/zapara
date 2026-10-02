package ru.bgtu_voenmeh.zapara.ui.groups

import org.junit.Assert.*
import org.junit.Test
import java.time.Instant
import java.time.ZoneId

class GroupHomeworkDeadlineTest {
    private val now = Instant.parse("2026-10-02T09:00:00Z")
    private val zone = ZoneId.of("Europe/Moscow")
    @Test fun exact_past_time_today_is_overdue_and_is_not_in_upcoming_days() {
        val deadline = now.minusSeconds(3600)
        assertTrue(sharedHomeworkDeadlineMatches(deadline, "overdue", now, zone))
        assertFalse(sharedHomeworkDeadlineMatches(deadline, "near", now, zone))
        assertTrue(sharedHomeworkDeadlineMatches(now.plusSeconds(3600), "near", now, zone))
    }
    @Test fun near_ends_at_the_local_midnight_after_tomorrow_and_null_is_separate() {
        assertTrue(sharedHomeworkDeadlineMatches(Instant.parse("2026-10-03T20:59:59Z"), "near", now, zone))
        assertFalse(sharedHomeworkDeadlineMatches(Instant.parse("2026-10-03T21:00:00Z"), "near", now, zone))
        assertTrue(sharedHomeworkDeadlineMatches(null, "none", now, zone))
        assertFalse(sharedHomeworkDeadlineMatches(null, "overdue", now, zone))
    }
}
