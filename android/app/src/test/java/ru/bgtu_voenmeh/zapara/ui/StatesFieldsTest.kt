package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import kotlin.math.max
import kotlin.math.min
import kotlin.math.pow

/** #109: состояния, поля и контраст (AN-09, AN-11, AN-12). */
class StatesFieldsTest {
    private fun src(path: String) = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/$path").readText()
    private val tokens = src("theme/Tokens.kt")

    private fun color(theme: String, name: String): Long {
        val block = tokens.substringAfter(theme).substringBefore("friends")
        return Regex("\\b$name = Color\\(0x([0-9A-F]{8})\\)").find(block)!!.groupValues[1].toLong(16)
    }
    private fun lum(argb: Long): Double {
        fun ch(v: Long) = (v / 255.0).let { if (it <= 0.03928) it / 12.92 else ((it + 0.055) / 1.055).pow(2.4) }
        return 0.2126 * ch(argb shr 16 and 0xFF) + 0.7152 * ch(argb shr 8 and 0xFF) + 0.0722 * ch(argb and 0xFF)
    }
    private fun ratio(a: Long, b: Long) = (max(lum(a), lum(b)) + 0.05) / (min(lum(a), lum(b)) + 0.05)

    @Test fun empty_state_action_is_the_primary_button() {
        val empty = src("components/Controls.kt").substringAfter("fun EmptyState(").substringBefore("fun Skeleton(")
        assertTrue(empty.contains("ZButton(actionText, onAction, Modifier.widthIn(max = 320.dp).fillMaxWidth())"))
        assertFalse(empty.contains("ghost = true"))
    }

    @Test fun no_group_schedule_header_has_neither_week_nor_a_second_group_button() {
        val schedule = src("schedule/ScheduleSection.kt")
        assertTrue(schedule.contains("LocalGroupPickInContent provides noGroup"))
        assertTrue(schedule.contains("if (!noGroup) ZButton(stringResource(R.string.nav_week)"))
        assertTrue(src("shell/ShellChrome.kt").contains("} else if (!LocalGroupPickInContent.current) {"))
    }

    @Test fun fields_have_a_visible_border_and_a_distinct_focus() {
        val field = src("components/Controls.kt")
        assertTrue(field.contains("focusedBorderColor = c.text1, unfocusedBorderColor = c.lineStrong,"))
        assertFalse(field.contains("unfocusedBorderColor = c.chip"))
    }

    @Test fun homework_badge_is_at_least_4_5_to_1_in_both_themes() {
        val chrome = src("shell/ShellChrome.kt")
        assertTrue(chrome.contains(".background(c.accent)") && chrome.contains("color = c.onAccent, maxLines = 1)"))
        for (theme in listOf("DarkColors", "LightColors")) {
            val r = ratio(color(theme, "accent"), color(theme, "onAccent"))
            assertTrue("$theme: $r", r >= 4.5)
        }
    }

    @Test fun overdue_is_a_bad_soft_pill_and_hints_are_not_warn_text() {
        val hw = src("homework/HomeworkSection.kt")
        assertTrue(hw.contains("val overdue = group.status == GroupStatus.Overdue && !item.done"))
        assertTrue(hw.contains(".background(c.badSoft)"))
        assertFalse(hw.contains("color = if (burning) c.warn else c.text2"))
        assertFalse(src("account/AccountUi.kt").contains("else Zapara.colors.warn)"))
        assertFalse(src("schedule/ScheduleSection.kt").contains("space_day_overlap), style = Zapara.typography.caption, color = Zapara.colors.warn"))
        assertTrue(src("schedule/LessonCard.kt").contains("semantics { contentDescription = dot }"))
        assertTrue(XmlCopy.get("lesson_hw_burning_dot").isNotBlank())
    }
}
