package ru.bgtu_voenmeh.zapara.ui.shell

import ru.bgtu_voenmeh.zapara.data.Homework
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.OffsetDateTime
import java.time.format.DateTimeParseException

object ShellLogic {
    fun isStale(value: String?, now: LocalDateTime): Boolean {
        if (value == null) return true
        val parsed = try { OffsetDateTime.parse(value).toLocalDateTime() }
        catch (_: DateTimeParseException) {
            try { LocalDateTime.parse(value) } catch (_: DateTimeParseException) { return true }
        }
        return parsed.isBefore(now.minusDays(7))
    }

    // Latest approved requirement is due today/tomorrow, NOT every overdue status.
    fun homeworkBadge(items: List<Homework>, today: LocalDate): Int = items.count {
        !it.done && it.status != "done" && (it.due == today || it.due == today.plusDays(1))
    }

    fun chip(groupName: String, odd: Boolean, copy: ru.bgtu_voenmeh.zapara.ui.UiCopy): String =
        copy.get("chip_group", groupName, copy.get(if (odd) "chip_odd" else "chip_even"))
}
