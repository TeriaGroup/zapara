package ru.bgtu_voenmeh.zapara.ui.calendar

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.nio.charset.StandardCharsets
import java.time.Instant
import java.time.OffsetDateTime

class CalendarExportTest {
    private val generated = Instant.parse("2026-10-01T20:00:00Z")
    private val start = OffsetDateTime.parse("2026-10-02T09:00:00+03:00").toInstant()
    private val end = OffsetDateTime.parse("2026-10-02T10:35:00+03:00").toInstant()
    private fun lesson() = CalendarEntry("3313|2026-10-02|Математика", start, end,
        "Математика", "320 УЛК")

    @Test fun uses_utc_instants_and_generation_stamp() {
        val result = CalendarExport.create(listOf(lesson()), "Неделя 09С52", generated)
        assertEquals(1, result.eventCount)
        assertEquals(0, result.skippedCount)
        assertTrue(result.content.contains("DTSTART:20261002T060000Z\r\nDTEND:20261002T073500Z"))
        assertTrue(result.content.contains("DTSTAMP:20261001T200000Z"))
        assertTrue(result.content.endsWith("END:VCALENDAR\r\n"))
    }

    @Test fun folds_utf8_without_splitting_russian_or_emoji_and_escapes_fields() {
        val title = "Длинное название 🚀 ".repeat(12)
        val result = CalendarExport.create(listOf(lesson().copy(summary = title,
            description = "Строка\nBEGIN:VEVENT; две, ещё\\путь")), title, generated)
        result.content.split("\r\n").filter(String::isNotEmpty).forEach {
            assertTrue(it.toByteArray(StandardCharsets.UTF_8).size <= 75)
        }
        val unfolded = result.content.replace("\r\n ", "")
        assertTrue(unfolded.contains("SUMMARY:$title"))
        assertTrue(unfolded.contains("DESCRIPTION:Строка\\nBEGIN:VEVENT\\; две\\, ещё\\\\путь"))
        assertEquals(1, unfolded.split("\r\n").count { it == "BEGIN:VEVENT" })
        assertFalse(result.content.contains("�"))
    }

    @Test fun stable_uid_deduplicates_occurrence_and_skips_bad_rows() {
        val first = lesson()
        val result = CalendarExport.create(listOf(first, first.copy(),
            first.copy(startInstant = first.startInstant.plusSeconds(604800),
                endInstant = first.endInstant.plusSeconds(604800)),
            first.copy(id = "bad", endInstant = first.startInstant.minusSeconds(60)),
            first.copy(id = "empty", summary = " ")), "Пары", generated)
        assertEquals(2, result.eventCount)
        assertEquals(3, result.skippedCount)
        val repeated = CalendarExport.create(listOf(first), "Пары", generated.plusSeconds(86400))
        assertEquals(result.content.split("\r\n").first { it.startsWith("UID:") },
            repeated.content.split("\r\n").first { it.startsWith("UID:") })
    }
}
