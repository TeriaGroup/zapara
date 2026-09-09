package ru.bgtu_voenmeh.zapara.ui.settings

import ru.bgtu_voenmeh.zapara.ui.UiCopy
import java.time.LocalDateTime
import java.time.OffsetDateTime
import java.time.format.DateTimeParseException
import java.time.temporal.ChronoUnit

object SettingsLogic {
    fun updatedLine(lastFetchedAt: String?, now: LocalDateTime, copy: UiCopy): String {
        val parsed = lastFetchedAt?.let(::parse) ?: return copy.get("settings_never_fetched")
        val stamp = "%02d.%02d %02d:%02d".format(parsed.dayOfMonth, parsed.monthValue, parsed.hour, parsed.minute)
        val days = ChronoUnit.DAYS.between(parsed.toLocalDate(), now.toLocalDate())
        val ago = if (days <= 0L) copy.get("settings_today_word")
        else copy.get("settings_ago", days, daysWord(days, copy))
        return copy.get("settings_updated", stamp, ago)
    }

    fun validateTimes(t1: String, t2: String, copy: UiCopy): String? {
        if (!HH_MM.matches(t1) || !HH_MM.matches(t2)) return copy.get("time_hhmm")
        val h1 = t1.substring(0, 2).toInt()
        val m1 = t1.substring(3, 5).toInt()
        val h2 = t2.substring(0, 2).toInt()
        val m2 = t2.substring(3, 5).toInt()
        if (h1 !in 0..23 || m1 !in 0..59 || h2 !in 0..23 || m2 !in 0..59) return copy.get("time_hhmm")
        return null
    }

    private val HH_MM = Regex("^\\d{2}:\\d{2}$")

    private fun parse(value: String): LocalDateTime? = try {
        OffsetDateTime.parse(value).toLocalDateTime()
    } catch (_: DateTimeParseException) {
        try { LocalDateTime.parse(value) } catch (_: DateTimeParseException) { null }
    }

    private fun daysWord(n: Long, copy: UiCopy): String {
        val mod10 = n % 10
        val mod100 = n % 100
        return copy.get(
            when {
                mod10 == 1L && mod100 != 11L -> "day_one"
                mod10 in 2L..4L && mod100 !in 12L..14L -> "day_few"
                else -> "day_many"
            }
        )
    }
}
