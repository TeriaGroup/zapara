package ru.bgtu_voenmeh.zapara.ui.calendar

import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.time.Instant
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter

data class CalendarEntry(
    val id: String,
    val startInstant: Instant,
    val endInstant: Instant,
    val summary: String,
    val location: String? = null,
    val description: String? = null
)

data class CalendarExportResult(val content: String, val eventCount: Int, val skippedCount: Int)
data class AllDayCalendarEntry(val id: String, val day: java.time.LocalDate?, val summary: String,
    val description: String? = null)

/** A static snapshot of real occurrences. No calendar account or recurrence is created. */
object CalendarExport {
    private val utcFormat = DateTimeFormatter.ofPattern("yyyyMMdd'T'HHmmss'Z'").withZone(ZoneOffset.UTC)
    private val hex = "0123456789abcdef".toCharArray()

    fun createAllDay(entries: Iterable<AllDayCalendarEntry>, name: String, generatedAt: Instant): CalendarExportResult {
        val lines = mutableListOf("BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//Zapara//Calendar//RU",
            "CALSCALE:GREGORIAN", "X-WR-CALNAME:${escape(name)}")
        val seen = HashSet<String>()
        var included = 0; var skipped = 0
        for (entry in entries) {
            val day = entry.day
            if (entry.id.isBlank() || entry.summary.isBlank() || day == null || day.year !in 1..9999 || day == java.time.LocalDate.of(9999, 12, 31)) {
                skipped++; continue
            }
            val start = day.format(DateTimeFormatter.BASIC_ISO_DATE)
            val end = day.plusDays(1).format(DateTimeFormatter.BASIC_ISO_DATE)
            val uid = sha256("${entry.id}\nDATE:$start")
            if (!seen.add(uid)) { skipped++; continue }
            included++
            lines += listOf("BEGIN:VEVENT", "UID:$uid", "DTSTAMP:${utcFormat.format(generatedAt)}",
                "DTSTART;VALUE=DATE:$start", "DTEND;VALUE=DATE:$end", "TRANSP:TRANSPARENT", "SUMMARY:${escape(entry.summary)}")
            entry.description?.takeIf(String::isNotBlank)?.let { lines += "DESCRIPTION:${escape(it)}" }
            lines += "END:VEVENT"
        }
        lines += "END:VCALENDAR"
        return CalendarExportResult(lines.joinToString("\r\n") { fold(it) } + "\r\n", included, skipped)
    }

    fun create(entries: Iterable<CalendarEntry>, name: String, generatedAt: Instant): CalendarExportResult {
        val lines = mutableListOf("BEGIN:VCALENDAR", "VERSION:2.0",
            "PRODID:-//\u0420\u0430\u0441\u043f\u0438\u0441\u0430\u043d\u0438\u0435 \u0432\u043e\u0435\u043d\u043c\u0435\u0445//\u0420\u0430\u0441\u043f\u0438\u0441\u0430\u043d\u0438\u0435//RU", "CALSCALE:GREGORIAN",
            "X-WR-CALNAME:${escape(name)}")
        val seen = HashSet<String>()
        var included = 0
        var skipped = 0
        for (entry in entries) {
            if (entry.id.isBlank() || entry.summary.isBlank() || !entry.endInstant.isAfter(entry.startInstant)) {
                skipped++
                continue
            }
            val uid = sha256("${entry.id}\n${utcFormat.format(entry.startInstant)}")
            if (!seen.add(uid)) {
                skipped++
                continue
            }
            included++
            lines += listOf("BEGIN:VEVENT", "UID:$uid", "DTSTAMP:${utcFormat.format(generatedAt)}",
                "DTSTART:${utcFormat.format(entry.startInstant)}", "DTEND:${utcFormat.format(entry.endInstant)}",
                "SUMMARY:${escape(entry.summary)}")
            entry.location?.takeIf(String::isNotBlank)?.let { lines += "LOCATION:${escape(it)}" }
            entry.description?.takeIf(String::isNotBlank)?.let { lines += "DESCRIPTION:${escape(it)}" }
            lines += "END:VEVENT"
        }
        lines += "END:VCALENDAR"
        return CalendarExportResult(lines.joinToString("\r\n") { fold(it) } + "\r\n", included, skipped)
    }

    private fun sha256(value: String): String = buildString(64) {
        for (byte in MessageDigest.getInstance("SHA-256").digest(value.toByteArray(StandardCharsets.UTF_8))) {
            val unsigned = byte.toInt() and 0xff
            append(hex[unsigned ushr 4])
            append(hex[unsigned and 15])
        }
    }

    private fun escape(value: String): String = buildString {
        var index = 0
        while (index < value.length) {
            val code = value.codePointAt(index)
            if (code == 9 || code == 10 || code == 13 || !Character.isISOControl(code)) appendCodePoint(code)
            index += Character.charCount(code)
        }
    }.replace("\\", "\\\\")
        .replace("\r\n", "\n").replace("\r", "\n")
        .replace("\n", "\\n").replace(";", "\\;").replace(",", "\\,")

    private fun fold(line: String): String = buildString {
        var index = 0
        var bytes = 0
        while (index < line.length) {
            val code = line.codePointAt(index)
            val text = String(Character.toChars(code))
            val width = text.toByteArray(StandardCharsets.UTF_8).size
            if (bytes + width > 75) {
                append("\r\n ")
                bytes = 1
            }
            append(text)
            bytes += width
            index += Character.charCount(code)
        }
    }
}
