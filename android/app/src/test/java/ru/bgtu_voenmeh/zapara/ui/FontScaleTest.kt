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
        // «Сегодня · Завтра · Послезавтра» при 2.0 ≈ 140 + 120 + 200 dp в строке 350 dp.
        assertTrue(FontFit.segmentsOverflow(listOf(140f, 120f, 200f), 0f, 350f))
        assertFalse(FontFit.segmentsOverflow(listOf(80f, 70f, 110f), 0f, 350f))
        assertTrue(src("ui/components/Controls.kt").contains("FlowRow(Modifier.fillMaxWidth().testTag(\"\$tag.Wrapped\"))"))
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
