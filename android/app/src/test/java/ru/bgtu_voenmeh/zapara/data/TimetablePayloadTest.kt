package ru.bgtu_voenmeh.zapara.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.LocalDate

class TimetablePayloadTest {
    private val xml = """<?xml version="1.0"?><Timetable>
  <Period Title="t" StartYear="2026" StartMonth="9" StartDay="1"/>
  <Weeks WeekCount="2"/>
</Timetable>"""

    @Test
    fun decodeXml_utf16le_bom() {
        val bom = byteArrayOf(0xFF.toByte(), 0xFE.toByte())
        val decoded = TimetablePayload.decodeXml(bom + xml.toByteArray(Charsets.UTF_16LE))
        assertEquals(xml, decoded)
        assertEquals(LocalDate.of(2026, 9, 1), GroupParser.parse(decoded).periodStart)
    }

    @Test
    fun decodeXml_utf16le_without_bom_uses_null_bytes() {
        val decoded = TimetablePayload.decodeXml(xml.toByteArray(Charsets.UTF_16LE))
        assertEquals(xml, decoded)
    }

    @Test
    fun decodeXml_utf8_bom_and_plain() {
        val bom = byteArrayOf(0xEF.toByte(), 0xBB.toByte(), 0xBF.toByte())
        assertEquals(xml, TimetablePayload.decodeXml(bom + xml.toByteArray(Charsets.UTF_8)))
        assertEquals(xml, TimetablePayload.decodeXml(xml.toByteArray(Charsets.UTF_8)))
    }

    @Test
    fun isOlderPeriod_only_when_last_good_exists() {
        val stored = LocalDate.of(2026, 9, 1)
        val older = LocalDate.of(2025, 9, 1)
        assertFalse(TimetablePayload.isOlderPeriod(older, stored, null))
        assertFalse(TimetablePayload.isOlderPeriod(older, stored, ""))
        assertTrue(TimetablePayload.isOlderPeriod(older, stored, "2026-09-10T12:00:00Z"))
        assertFalse(TimetablePayload.isOlderPeriod(stored, stored, "2026-09-10T12:00:00Z"))
        assertFalse(TimetablePayload.isOlderPeriod(stored, older, "2026-09-10T12:00:00Z"))
    }
}
