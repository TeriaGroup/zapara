package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.components.FontFit
import java.io.File

/** #100 / AN-02: крупный шрифт (1.3 / 2.0) — подписи панели, вкладки дней, слова в кнопках. */
class FontScaleTest {
    private fun src(path: String) = File("src/main/java/ru/bgtu_voenmeh/zapara/$path").readText()

    @Test fun bottom_bar_never_drops_labels() {
        val bar = src("ui/shell/ShellChrome.kt")
        assertFalse("режима «только значки» больше нет", bar.contains("iconOnly"))
        assertTrue(bar.contains("FontFit.barLabelScale"))
    }

    @Test fun bar_labels_grow_to_130_percent_then_stop() {
        // Ячейка 78 dp, подпись «Расписание» при 1.0 ≈ 60 dp — растёт как шрифт.
        assertEquals(1f, FontFit.barLabelScale(1f, 60f, 78f), 0.001f)
        assertEquals(1f, FontFit.barLabelScale(1.3f, 60f * 1.3f, 78f), 0.001f)
        // При 2.0 подпись уже вдвое больше: потолок 1.3× => множитель 0.65, и 120 * 0.65 = 78 влезает.
        val s = FontFit.barLabelScale(2f, 120f, 78f)
        assertEquals(0.65f, s, 0.001f)
        assertTrue(120f * s <= 78f + 0.01f)
    }

    @Test fun bar_labels_shrink_to_fit_but_stay_readable() {
        val s = FontFit.barLabelScale(1f, 100f, 78f)
        assertEquals(0.78f, s, 0.001f)
        assertEquals(FontFit.MIN_SCALE, FontFit.barLabelScale(1f, 1000f, 78f), 0.001f)
    }

    @Test fun mid_word_break_is_detected() {
        val t = "Пользовательское соглашение"
        assertTrue("«Пользовательско/е»", FontFit.breaksMidWord(t, listOf(15, t.length)))
        assertFalse("перенос по пробелу", FontFit.breaksMidWord(t, listOf(17, t.length)))
        assertFalse("одна строка", FontFit.breaksMidWord(t, listOf(t.length)))
        assertFalse("после дефиса можно", FontFit.breaksMidWord("ВЦ-3 ГК", listOf(3, 7)))
    }

    @Test fun day_tabs_wrap_instead_of_hiding_behind_the_edge() {
        assertTrue(src("ui/components/Controls.kt").contains("FontFit.segmentRows(natural, available)"))
        assertFalse("ряд больше не прячется в прокрутке", src("ui/components/Controls.kt").substringAfter("fun ZSegmented").substringBefore("fun ZSwitch").contains("horizontalScroll"))
    }

    @Test fun segment_rows_fill_the_width_and_wrap_only_when_needed() {
        // Влезает: одна строка, каждому по трети.
        assertEquals(listOf(117 to 0, 117 to 0, 116 to 0), FontFit.segmentRows(listOf(117, 117, 116), 350))
        // «Послезавтра» шире трети: перенос, вторая строка во всю ширину, первая заполнена.
        val wrapped = FontFit.segmentRows(listOf(117, 117, 140), 350)
        assertEquals(listOf(0, 0, 1), wrapped.map { it.second })
        assertEquals(350, wrapped[0].first + wrapped[1].first)
        assertEquals(350, wrapped[2].first)
        // Слишком длинный сегмент не шире ряда.
        assertEquals(350, FontFit.segmentRows(listOf(500), 350)[0].first)
    }

    @Test fun buttons_keep_words_whole_and_header_chip_has_room() {
        assertTrue(src("ui/theme/Primitives.kt").contains("WordFitText(text"))
        assertTrue(src("ui/shell/ShellChrome.kt").contains("padding(horizontal = Zapara.space.l, vertical = Zapara.space.xs)"))
    }

    @Test fun clickable_chips_keep_a_48dp_target() {
        // Чип аудитории (LessonCard) — кликабельный ZChip; 43 dp в аудите 1.3 — это край экрана под панелью.
        val chip = src("ui/components/Controls.kt")
        assertTrue(chip.contains("minHeight = if (onClick != null) Zapara.space.minTouch else 0.dp"))
        assertTrue(src("ui/schedule/LessonCard.kt").contains("ZChip(lesson.room, modifier = Modifier.align(Alignment.CenterVertically), onClick = onRoom"))
    }
}
