package ru.bgtu_voenmeh.zapara.ui.schedule

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.XmlCopy
import java.io.File
import java.time.LocalTime

/** #108: «Сегодня», лист пары и шапка (AN-08, AN-15, AN-23). */
class TodayLessonSheetTest {
    private val ui = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/schedule")
    private fun src(name: String) = File(ui, name).readText()
    private fun lesson(start: String, end: String, room: String = "229 ГК", remote: Boolean = false) =
        LessonUi(1, start, end, "практика", "Физика", null, "Петров Н. Н.", room, room, null,
            emptyList(), emptyList(), false, "Физика", "физика", remote = remote)
    private val names = mapOf(R.string.schedule_now_hero to "schedule_now_hero",
        R.string.schedule_now_hero_no_room to "schedule_now_hero_no_room")
    private val text: (Int, Array<out Any>) -> String = { id, args -> XmlCopy.get(names.getValue(id), *args) }

    @Test fun hero_names_the_live_pair_room_and_end() {
        val day = listOf(lesson("09:00", "10:30"), lesson("10:50", "12:20"))
        val live = TodayHero.live(day, LocalTime.of(11, 5))!!
        assertEquals("10:50", live.timeStart)
        assertEquals("Сейчас · Физика · 229 ГК · до 12:20", TodayHero.line(live, text))
        assertEquals("Сейчас · Физика · до 12:20", TodayHero.line(lesson("10:50", "12:20", remote = true), text))
    }

    @Test fun no_hero_before_classes_in_a_break_or_after_the_end() {
        val day = listOf(lesson("09:00", "10:30"), lesson("10:50", "12:20"))
        assertNull(TodayHero.live(day, LocalTime.of(8, 0)))
        assertNull(TodayHero.live(day, LocalTime.of(10, 40)))
        assertNull(TodayHero.live(day, LocalTime.of(12, 20)))
    }

    @Test fun hero_is_pinned_framed_and_opens_the_sheet() {
        val s = src("ScheduleSection.kt")
        assertTrue(s.contains("stickyHeader(key = \"Schedule.NowHero\")"))
        assertTrue(s.contains("border(1.dp, Zapara.colors.lineStrong"))
        assertTrue(s.contains("onClick = { onEvent(ScheduleEvent.LongPress(live)) }, tag = \"Schedule.NowHero\""))
    }

    @Test fun past_pairs_start_collapsed_behind_one_row() {
        val s = src("ScheduleSection.kt")
        assertTrue(s.contains("mutableStateOf(pastCount > 0)"))
        assertEquals("Показать прошедшие пары (2)", XmlCopy.get("schedule_show_past", 2))
        assertEquals(1, Regex("tag = \"Schedule.RemainingOnly\"").findAll(s).count())
    }

    @Test fun one_day_switcher_and_compact_time() {
        val strip = src("DateStrip.kt")
        assertFalse("сегмент «Сегодня/Завтра/Послезавтра» убран", strip.contains("ZSegmented("))
        assertTrue(strip.contains("tag = \"Schedule.BackToToday\""))
        assertTrue("8 dp под шапкой", strip.contains("padding(top = Zapara.space.s, bottom = Zapara.space.s)"))
        val card = src("LessonCard.kt")
        assertTrue(card.contains("\"\${lesson.timeStart}–\${lesson.timeEnd}\""))
        assertFalse(card.contains("\"\${lesson.timeStart} – \${lesson.timeEnd}\""))
    }

    @Test fun tapping_the_card_opens_the_sheet() {
        assertTrue(src("LessonCard.kt").contains("onClick = onLongClick,"))
    }

    @Test fun sheet_has_chips_and_actions_in_order_with_rename_last() {
        val s = src("LessonActionsSheet.kt")
        assertTrue(s.contains("LessonTypeChip(lesson.type, \"Actions.Type\")"))
        assertTrue(s.contains("ZChip(lesson.teacher, tag = \"Actions.Teacher\")"))
        val order = listOf("Actions.Map", "Actions.Homework", "Actions.Discuss", "Actions.Share", "Actions.Rename")
            .map { s.indexOf("\"$it\"") }
        assertTrue(order.all { it > 0 })
        assertEquals(order.sorted(), order)
        assertEquals("Переименовать у меня", XmlCopy.get("lesson_rename_mine"))
    }

    @Test fun parity_is_spelled_out_once_in_sentence_case() {
        assertEquals("Чётная, 6-я неделя", LessonFormat.weekLine(odd = false, weekNumber = 6, copy = XmlCopy))
        assertEquals("Нечётная, 7-я неделя", LessonFormat.weekLine(odd = true, weekNumber = 7, copy = XmlCopy))
        assertTrue(src("ScheduleSection.kt").contains("page.weekLine.ifBlank"))
    }

    @Test fun header_chip_is_full_when_it_fits_short_only_to_avoid_a_second_row() {
        val sl = ru.bgtu_voenmeh.zapara.ui.shell.ShellLogic
        assertEquals("И831Б · чётная", sl.chip("И831Б", odd = false, copy = XmlCopy))
        assertEquals("И831Б · чёт.", sl.chipShort("И831Б", odd = false, copy = XmlCopy))
        // заголовок 150, «Неделя» 80, чип 150/105, зазор 8, ширина 358
        assertEquals(2, sl.chipVariant(title = 150, actions = 0, full = 150, short = 105, gap = 8, max = 358))
        assertEquals(3, sl.chipVariant(title = 150, actions = 80, full = 150, short = 105, gap = 8, max = 358))
        assertEquals("и короткий не помещается — полный", 2, sl.chipVariant(title = 200, actions = 80, full = 150, short = 105, gap = 8, max = 358))
    }

    @Test fun jumps_skip_the_sticky_now_header_too() {
        // [Сейчас]? [блок дня] [пары…]: при закреплённом «Сейчас» пара i — элемент i + 2, без него — i + 1.
        assertEquals(1, ScheduleListIndex.lesson(0, nowHeader = false))
        assertEquals(2, ScheduleListIndex.lesson(0, nowHeader = true))
        assertEquals(5, ScheduleListIndex.lesson(3, nowHeader = true))
        val s = src("ScheduleSection.kt")
        assertFalse("переходы считают смещение через ScheduleListIndex", Regex("animateScrollToItem\\([^)]*\\+ 1\\)").containsMatchIn(s))
        assertEquals(4, Regex("animateScrollToItem\\(ScheduleListIndex\\.lesson\\([^\\n]*nowHeader = live != null\\)\\)").findAll(s).count())
        assertTrue(s.contains("if (live != null) stickyHeader(key = \"Schedule.NowHero\")"))
    }

    @Test fun only_the_placed_group_chip_carries_the_tag() {
        val chrome = java.io.File("src/main/java/ru/bgtu_voenmeh/zapara/ui/shell/ShellChrome.kt").readText()
        val bar = chrome.substring(chrome.indexOf("fun ZTopBar("), chrome.indexOf("private enum class TopBarSlot"))
        // пробы меряют ширину без семантики, тег — только у выбранной формы
        assertEquals(2, Regex("clearAndSetSemantics \\{\\}\\) \\{ GroupChip\\([^)]*tagged = false\\)").findAll(bar).count())
        assertEquals(1, Regex("GroupChip\\([^)]*tagged = true\\)").findAll(bar).count())
        assertFalse(bar.contains("\"Top.GroupChip\""))
        val chip = chrome.substring(chrome.indexOf("private fun GroupChip("))
        assertTrue(chip.contains("val tag = if (tagged) \"Top.GroupChip\" else null"))
        assertEquals(1, Regex("\"Top.GroupChip\"").findAll(chrome).count())
    }
}
