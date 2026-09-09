package ru.bgtu_voenmeh.zapara.ui.week

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.GROUP_FIXTURE
import ru.bgtu_voenmeh.zapara.data.GroupParser
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import ru.bgtu_voenmeh.zapara.ui.XmlCopy
import java.time.LocalDate

class WeekComposerTest {
    private val parsed by lazy { GroupParser.parse(GROUP_FIXTURE) }
    private val today = LocalDate.of(2026, 9, 8)
    private val ctx = SchedCtx("3313", LocalDate.of(2026, 9, 1), 2, false)
    private val mathNorm = Parity.normalizeSubject("лек ВЫСШ. МАТЕМАТ")

    @Test fun odd_week_six_days_and_lesson_counts() {
        val days = WeekComposer.compose(1, parsed.lessons, { norm, _ -> if (norm == mathNorm) "Матан" else "" }, ctx, today, XmlCopy)
        assertEquals(6, days.size)
        // 2026-09-14 is XML-even (week 2); next user-odd Monday is 21.09 (week 3).
        assertEquals("Понедельник · 21.09", days[0].title)
        assertEquals(2, days[0].rows.size)
        assertEquals(0, days.first { it.dow == 4 }.rows.size)
    }

    @Test fun nearest_current_tuesday_is_today() {
        val current = if (Parity.isOddWeek(today, ctx.periodStart, ctx.weekCount, ctx.invert)) 1 else 2
        assertEquals(2, current)
        assertEquals(today, WeekComposer.nearestDate(2, current, ctx, today))
        val days = WeekComposer.compose(current, parsed.lessons, { _, _ -> "" }, ctx, today, XmlCopy)
        assertTrue(days.first { it.dow == 2 }.isToday)
    }

    @Test fun inverted_odd_lands_on_xml_even_dates() {
        val inverted = ctx.copy(invert = true)
        assertEquals(LocalDate.of(2026, 9, 14), WeekComposer.nearestDate(1, 1, inverted, today))
    }
}
