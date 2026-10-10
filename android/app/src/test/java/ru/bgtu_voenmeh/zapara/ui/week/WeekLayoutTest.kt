package ru.bgtu_voenmeh.zapara.ui.week

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import java.time.LocalDate

/** #102 (AN-05): шапка «Недели» в одну строку, плотные дни, «Перерыв», неразрывные «С++» и «ВЦ-3». */
class WeekLayoutTest {
    private val section = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/week/WeekSection.kt").readText()

    @Test fun range_is_compact_and_keeps_day_with_month() {
        assertEquals("5–11\u00A0окт.", WeekHeader.range(LocalDate.of(2026, 10, 5), LocalDate.of(2026, 10, 11)))
        assertEquals("28\u00A0сент. – 4\u00A0окт.", WeekHeader.range(LocalDate.of(2026, 9, 28), LocalDate.of(2026, 10, 4)))
        assertEquals("", WeekHeader.range(null, null))
    }

    @Test fun caption_carries_parity_in_lower_case() {
        assertEquals("5–11\u00A0окт. ·\u00A0чётная", WeekHeader.caption("5–11\u00A0окт.", "Чётная"))
        assertEquals("нечётная", WeekHeader.caption("", "Нечётная"))
    }

    @Test fun cpp_and_hyphenated_rooms_do_not_break() {
        assertEquals("Программирование на C\u2060+\u2060+", WeekHeader.keepCpp("Программирование на C++"))
        assertEquals("С\u2060+\u2060+", WeekHeader.keepCpp("С++"))
        assertEquals("ВЦ\u20113", WeekHeader.noBreakRoom("ВЦ-3"))
        assertEquals("229 ГК", WeekHeader.noBreakRoom("229 ГК"))
        assertEquals("лек - пр", WeekHeader.noBreakRoom("лек - пр"))
        // На экране тот же текст: убираем невидимые символы и неразрывный дефис — исходная строка.
        assertEquals("ВЦ-3", WeekHeader.noBreakRoom("ВЦ-3").replace('\u2011', '-'))
    }

    @Test fun hyphenated_lesson_names_still_wrap_only_rooms_keep_the_hyphen() {
        // #113 follow-up: при fontScale 2.0 «Научно-исследовательская» переносится по дефису.
        assertEquals("Научно-исследовательская работа", WeekHeader.keepCpp("Научно-исследовательская работа"))
        assertFalse(WeekHeader.keepCpp("Научно-исследовательская").contains('\u2011'))
        assertEquals("А\u2011101", WeekHeader.noBreakRoom("А-101"))
        assertTrue(section.contains("Text(WeekHeader.keepCpp(row.name),"))
        assertTrue(section.contains("Text(WeekHeader.noBreakRoom(row.room),"))
        assertFalse(section.contains("WeekHeader.noBreak("))
    }

    @Test fun one_header_row_with_tools_and_share_in_the_overflow_menu() {
        val menu = section.substringAfter("\"Week.More\")").substringBefore("if (largeText) {")
        listOf("Week.PickDate", "Week.BrowseTools", "Week.ShareOpen").forEach { assertTrue("$it в «⋯»", menu.contains(it)) }
        assertFalse("нет отдельного сегмента чётности", section.contains("ZSegmented(labels"))
        assertFalse("нет строки «Всего пар»", section.contains("R.string.next_week_total"))
        assertFalse("«Поделиться» не строкой", section.contains("ZActionButton(stringResource(R.string.ux300_visual_week_share)"))
        assertTrue("при крупном шрифте — не больше двух строк", section.contains("// При fontScale ≥ 1.5 — не больше двух строк"))
    }

    @Test fun day_title_opens_the_day_and_a_chevron_collapses_it() {
        assertFalse(section.contains("ZActionButton(stringResource(R.string.ux300_android_week_open_day)"))
        assertFalse(section.contains("ZDisclosureButton(stringResource(if (collapsed && query.isBlank())"))
        assertTrue(section.contains(".testTag(\"Week.OpenDay.\${day.dow}\")"))
        assertTrue(section.contains("\"Week.Collapse.\${day.dow}\""))
    }

    @Test fun gaps_are_called_breaks() {
        val res = File("src/main/res/values").listFiles()!!.joinToString("\n") { it.readText() }
        assertTrue(res.contains("<string name=\"ux300_android_week_free_window\">Перерыв %1\$d мин</string>"))
        assertFalse(res.contains("Свободное окно"))
    }
}
