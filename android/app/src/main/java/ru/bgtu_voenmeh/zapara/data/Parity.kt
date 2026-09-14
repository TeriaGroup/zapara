package ru.bgtu_voenmeh.zapara.data

import java.time.LocalDate
import java.time.temporal.ChronoUnit

// Week containing 1 September is week 1 = odd. Later weeks are Mon–Sun, odd then even.
object Parity {

    fun mondayOfWeekContainingStart(periodStart: LocalDate): LocalDate {
        val dow = periodStart.dayOfWeek.value // Monday=1 .. Sunday=7
        return periodStart.minusDays((dow - 1).toLong())
    }

    fun weekNumber(date: LocalDate, periodStart: LocalDate): Int {
        val monday = mondayOfWeekContainingStart(periodStart)
        val days = ChronoUnit.DAYS.between(monday, date).toInt()
        if (days < 0) return 1
        return days / 7 + 1
    }

    fun weekCode(date: LocalDate, periodStart: LocalDate, weekCount: Int = 2): Int {
        var code = weekNumber(date, periodStart) % weekCount
        if (code == 0) code = weekCount
        return code
    }

    fun isOddWeek(date: LocalDate, periodStart: LocalDate, weekCount: Int = 2, invert: Boolean = false): Boolean {
        val isOdd = weekCode(date, periodStart, weekCount) == 1
        return if (invert) !isOdd else isOdd
    }

    fun normalizeSubject(raw: String?): String {
        if (raw.isNullOrBlank()) return ""
        return raw.trim().lowercase().replace('ё', 'е')
            .split(Regex("\\s+")).filter { it.isNotEmpty() }.joinToString(" ")
    }

    fun dayTitleToNumber(title: String?): Int = when (title?.trim()?.lowercase()) {
        "понедельник" -> 1
        "вторник" -> 2
        "среда" -> 3
        "четверг" -> 4
        "пятница" -> 5
        "суббота" -> 6
        "воскресенье" -> 7
        else -> 0
    }

    fun dayNumberToTitle(n: Int): String = when (n) {
        1 -> "Понедельник"
        2 -> "Вторник"
        3 -> "Среда"
        4 -> "Четверг"
        5 -> "Пятница"
        6 -> "Суббота"
        7 -> "Воскресенье"
        else -> ""
    }
}
