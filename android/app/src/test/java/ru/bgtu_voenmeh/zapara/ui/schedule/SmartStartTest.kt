package ru.bgtu_voenmeh.zapara.ui.schedule

import org.junit.Assert.assertEquals
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Lesson
import java.time.LocalDate
import java.time.LocalDateTime

class SmartStartTest {
    private fun lesson(start: String, end: String) = Lesson(timeStart = start, timeEnd = end)

    @Test fun before_the_last_lesson_ends_it_is_today() {
        assertEquals(
            LocalDate.of(2026, 9, 8),
            SmartStart.initialDate(
                LocalDateTime.of(2026, 9, 8, 10, 0),
                listOf(lesson("09:00", "10:35"), lesson("12:40", "14:15"))
            )
        )
    }

    @Test fun fifteen_minutes_after_the_last_lesson_it_is_tomorrow() {
        assertEquals(
            LocalDate.of(2026, 9, 9),
            SmartStart.initialDate(
                LocalDateTime.of(2026, 9, 8, 14, 31),
                listOf(lesson("09:00", "10:35"), lesson("12:40", "14:15"))
            )
        )
        assertEquals(
            LocalDate.of(2026, 9, 8),
            SmartStart.initialDate(
                LocalDateTime.of(2026, 9, 8, 14, 29),
                listOf(lesson("12:40", "14:15"))
            )
        )
    }

    @Test fun a_day_without_lessons_stays_today_and_sunday_goes_to_monday() {
        assertEquals(
            LocalDate.of(2026, 9, 8),
            SmartStart.initialDate(LocalDateTime.of(2026, 9, 8, 20, 0), emptyList())
        )
        assertEquals(
            LocalDate.of(2026, 9, 7),
            SmartStart.initialDate(LocalDateTime.of(2026, 9, 6, 12, 0), emptyList())
        )
    }
}
