package ru.bgtu_voenmeh.zapara.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.LocalDate

class ParityTest {

    private val ps = LocalDate.of(2026, 9, 1)

    @Test
    fun probeDatesMatchWindowsRecon() {
        // (date, weekNumber, code, isOdd) — mirrors A0 probe.py
        val cases = listOf(
            Triple(LocalDate.of(2026, 8, 31), 1 to 1, true),
            Triple(LocalDate.of(2026, 9, 1), 1 to 1, true),
            Triple(LocalDate.of(2026, 9, 3), 1 to 1, true),
            Triple(LocalDate.of(2026, 9, 6), 1 to 1, true),
            Triple(LocalDate.of(2026, 9, 7), 2 to 2, false),
            Triple(LocalDate.of(2026, 9, 8), 2 to 2, false),
            Triple(LocalDate.of(2026, 9, 13), 2 to 2, false),
            Triple(LocalDate.of(2026, 9, 14), 3 to 1, true),
            Triple(LocalDate.of(2026, 9, 15), 3 to 1, true)
        )
        for ((date, wnCode, odd) in cases) {
            assertEquals("weekNumber $date", wnCode.first, Parity.weekNumber(date, ps))
            assertEquals("weekCode $date", wnCode.second, Parity.weekCode(date, ps))
            assertEquals("isOdd $date", odd, Parity.isOddWeek(date, ps))
        }
    }

    @Test
    fun invertFlipsParity() {
        val d = LocalDate.of(2026, 9, 3)
        assertTrue(Parity.isOddWeek(d, ps, invert = false))
        assertFalse(Parity.isOddWeek(d, ps, invert = true))
    }

    @Test
    fun normalizeSubject() {
        assertEquals("лек высш. математика", Parity.normalizeSubject("  лек   ВЫСШ. МАТЕМАТИКА "))
        assertEquals("лек елка", Parity.normalizeSubject("лек ЁЛКА"))
        assertEquals("", Parity.normalizeSubject(null))
        assertEquals("", Parity.normalizeSubject("   "))
    }

    @Test
    fun dayMapping() {
        assertEquals(1, Parity.dayTitleToNumber("Понедельник"))
        assertEquals(6, Parity.dayTitleToNumber("Суббота"))
        assertEquals(0, Parity.dayTitleToNumber("???"))
        assertEquals("Среда", Parity.dayNumberToTitle(3))
    }
}
