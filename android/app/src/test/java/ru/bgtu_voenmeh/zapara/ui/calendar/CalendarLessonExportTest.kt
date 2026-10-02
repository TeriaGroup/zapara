package ru.bgtu_voenmeh.zapara.ui.calendar

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.week.WeekDayUi
import ru.bgtu_voenmeh.zapara.ui.week.WeekRowUi
import java.time.Instant
import java.time.LocalDate

class CalendarLessonExportTest {
    @Test fun canonical_id_uses_raw_utf16_lengths_like_other_clients() {
        assertEquals("4:331310:Математика6:Петров5:320*;",
            CalendarLessonExport.canonicalId("3313", "Математика", "Петров", "320*;"))
    }
    @Test fun selected_seven_day_week_uses_absolute_dates_and_moscow_to_utc() {
        val monday = LocalDate.of(2026, 10, 5)
        val days = (0L..6L).map { offset ->
            val date = monday.plusDays(offset)
            WeekDayUi(date.dayOfWeek.value, date.toString(), date,
                if (offset == 0L) listOf(WeekRowUi("09:00–10:35", "Математика", "320 УЛК",
                    start = "09:00", end = "10:35", subjectNorm = "математика")) else emptyList(), false)
        }
        val lessons = CalendarLessonExport.week(days)
        assertEquals(monday, lessons.single().date)
        val result = CalendarLessonExport.ics(lessons, "3313", "Неделя", Instant.parse("2026-10-01T20:00:00Z"))
        assertEquals(1, result.eventCount)
        assertEquals(0, result.skippedCount)
        assertTrue(result.content.contains("DTSTART:20261005T060000Z"))
        assertTrue(CalendarLessonExport.plainText("Неделя", lessons).contains("5 октября 2026"))
    }

    @Test fun malformed_or_nonpositive_times_are_reported_without_exporting_event() {
        val date = LocalDate.of(2026, 10, 5)
        val lessons = listOf(CalendarLesson(date, "неизвестно", "10:35", "А", "а"),
            CalendarLesson(date, "11:00", "10:00", "Б", "б"))
        val result = CalendarLessonExport.ics(lessons, "3313", "День", Instant.parse("2026-10-01T20:00:00Z"))
        assertEquals(0, result.eventCount)
        assertEquals(2, result.skippedCount)
    }
}
