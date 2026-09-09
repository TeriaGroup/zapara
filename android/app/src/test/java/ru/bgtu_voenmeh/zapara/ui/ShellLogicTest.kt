package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Homework
import ru.bgtu_voenmeh.zapara.ui.shell.ShellLogic
import java.time.LocalDate
import java.time.LocalDateTime

class ShellLogicTest {
    private val now = LocalDateTime.of(2026, 9, 8, 12, 0)

    @Test fun stale_after_seven_days() {
        assertFalse(ShellLogic.isStale("2026-09-02T10:00:00", now))
        assertTrue(ShellLogic.isStale("2026-08-31T10:00:00", now))
        assertTrue(ShellLogic.isStale(null, now))
        assertTrue(ShellLogic.isStale("garbage", now))
    }

    @Test fun group_chip_text() {
        assertEquals("А863С · нечёт.", ShellLogic.chip("А863С", odd = true, XmlCopy))
        assertEquals("А863С · чёт.", ShellLogic.chip("А863С", odd = false, XmlCopy))
    }

    @Test fun homework_badge_counts_due_today_or_tomorrow() {
        val today = LocalDate.of(2026, 9, 8)
        fun hw(due: LocalDate?, done: Boolean = false, status: String = "pending") =
            Homework(1, "n", "t", LocalDate.of(2026, 9, 1), 1, due, status, done)
        val list = listOf(
            hw(today),
            hw(today.plusDays(1)),
            hw(today.plusDays(2)),
            hw(today, done = true),
            hw(today, status = "done")
        )
        assertEquals(2, ShellLogic.homeworkBadge(list, today))
        assertEquals(0, ShellLogic.homeworkBadge(emptyList(), today))
    }
}
