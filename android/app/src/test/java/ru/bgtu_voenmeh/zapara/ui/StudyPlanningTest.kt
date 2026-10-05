package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.calendar.AllDayCalendarEntry
import ru.bgtu_voenmeh.zapara.ui.calendar.CalendarExport
import java.time.LocalDate
import java.time.Instant

class StudyPlanningTest {
    @Test fun free_windows_merge_busy_intervals_and_reject_unknown_days() {
        val first = listOf(StudyInterval("9:00", "10:00"), StudyInterval("12:00", "13:00"))
        assertEquals(listOf(FreeStudyInterval("10:30", "11:30", 60)), StudyPlanning.commonFreeIntervals(first,
            listOf(StudyInterval("09:30", "10:30"), StudyInterval("11:30", "12:30"))))
        assertTrue(StudyPlanning.commonFreeIntervals(first, emptyList()).isEmpty())
        assertTrue(StudyPlanning.commonFreeIntervals(first, listOf(StudyInterval("09:00", "bad"))).isEmpty())
        assertTrue(StudyPlanning.commonFreeIntervals(first, listOf(StudyInterval("09:30", "10:30"),
            StudyInterval("10:15", "11:45"), StudyInterval("12:00", "12:30")), 20).isEmpty())
    }
    @Test fun transfer_requires_valid_time_and_route() {
        assertEquals("tight", StudyPlanning.assessTransfer("10:00", "10:05", 301).status)
        assertEquals("fits", StudyPlanning.assessTransfer("10:00", "10:05", 300).status)
        assertEquals("unknown", StudyPlanning.assessTransfer("10:00", "10:05", null).status)
        assertEquals("overlap", StudyPlanning.assessTransfer("10:00", "09:55", 0).status)
    }
    @Test fun all_day_is_date_only_and_rolls_year_without_timezone() {
        val entry = AllDayCalendarEntry("task", LocalDate.of(2026,12,31), "Task")
        val result = CalendarExport.createAllDay(listOf(entry, entry, entry.copy(day = null)), "Plan", Instant.EPOCH)
        assertEquals(1, result.eventCount); assertEquals(2, result.skippedCount)
        assertTrue(result.content.contains("DTSTART;VALUE=DATE:20261231\r\nDTEND;VALUE=DATE:20270101"))
    }
}
