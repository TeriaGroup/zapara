package ru.bgtu_voenmeh.zapara.data

import java.time.LocalDate
import java.time.temporal.ChronoUnit
import java.util.Locale

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
        // Match the server and Windows normalizer: invariant case, ASCII whitespace only.
        // Locale-dependent lowercase or Unicode \\s would reject a valid timetable body.
        return raw.trim().lowercase(Locale.ROOT).replace('ё', 'е')
            .split(' ', '\t', '\r', '\n').filter { it.isNotEmpty() }.joinToString(" ")
    }

    fun subjectMatchKey(raw: String?): String =
        normalizeSubject(raw).replace(Regex("[^\\p{L}\\p{N}]+"), "")

    fun sameSubject(a: String?, b: String?): Boolean {
        val x = subjectMatchKey(a)
        val y = subjectMatchKey(b)
        if (x.isEmpty() || y.isEmpty()) return false
        if (x == y) return true
        val n = minOf(x.length, y.length)
        return n >= 8 && (x.startsWith(y) || y.startsWith(x))
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

    // WeekCode 1/2 wins. Empty/0/invalid falls back to Time ("9:00 Нечетная").
    fun parseXmlParity(weekCode: String?, timeRaw: String?): Int {
        val fromCode = weekCode?.trim()?.toIntOrNull()
        if (fromCode != null && fromCode in 1..2) return fromCode
        val t = (timeRaw ?: "").lowercase().replace('ё', 'е')
        return when {
            "нечетн" in t -> 1
            "четн" in t -> 2
            else -> 0
        }
    }
}
