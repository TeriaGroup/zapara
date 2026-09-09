package ru.bgtu_voenmeh.zapara.ui.schedule

import ru.bgtu_voenmeh.zapara.data.Lesson
import java.time.DayOfWeek
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime

object SmartStart {
    fun initialDate(now: LocalDateTime, lessonsToday: List<Lesson>): LocalDate {
        val today = now.toLocalDate()
        if (today.dayOfWeek == DayOfWeek.SUNDAY) return today.plusDays(1)
        val lastEnd = lessonsToday.mapNotNull { runCatching { LocalTime.parse(it.timeEnd) }.getOrNull() }.maxOrNull()
            ?: return today
        return if (now.toLocalTime().isAfter(lastEnd.plusMinutes(15))) today.plusDays(1) else today
    }
}
