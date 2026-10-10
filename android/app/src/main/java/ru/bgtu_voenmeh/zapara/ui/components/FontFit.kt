package ru.bgtu_voenmeh.zapara.ui.components

import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.TextUnit
import androidx.compose.ui.unit.isSpecified

/**
 * #100 / AN-02: крупный шрифт (fontScale 1.3–2.0) не должен прятать подписи и рвать слова посреди.
 * Чистые правила — здесь, чтобы их проверяли JVM-тесты без Compose.
 */
object FontFit {
    /** Подписи нижней панели растут вместе со шрифтом до этого множителя, дальше — не больше. */
    const val BAR_LABEL_MAX_SCALE = 1.3f
    /** Ниже этого подписи и кнопки не ужимаются: лучше перенос по словам, чем нечитаемый текст. */
    const val MIN_SCALE = 0.6f

    /**
     * Множитель к уже увеличенной (fontScale) подписи панели: сначала потолок [BAR_LABEL_MAX_SCALE],
     * затем — чтобы самая длинная подпись влезла в ячейку. Одинаков для всех пунктов, чтобы панель была ровной.
     */
    fun barLabelScale(fontScale: Float, widestLabel: Float, cell: Float): Float {
        val cap = if (fontScale > BAR_LABEL_MAX_SCALE) BAR_LABEL_MAX_SCALE / fontScale else 1f
        val fit = if (widestLabel * cap > cell && widestLabel > 0f) cell / widestLabel else cap
        return minOf(cap, fit).coerceAtLeast(MIN_SCALE * cap)
    }

    /** Строка оборвана внутри слова: последний символ строки и первый символ следующей — буквы или цифры. */
    fun breaksMidWord(text: CharSequence, lineEnds: List<Int>): Boolean = lineEnds.dropLast(1).any { end ->
        end in 1 until text.length && text[end - 1].isLetterOrDigit() && text[end].isLetterOrDigit()
    }

    /**
     * Раскладка сегментов по строкам: [natural] — собственные ширины (уже не меньше трети ряда), [available] — ширина ряда.
     * Всё влезает — одна строка; иначе жадно по строкам. Остаток строки делится поровну, так что строка всегда заполнена.
     * Возвращает ширину каждого сегмента и номер его строки.
     */
    fun segmentRows(natural: List<Int>, available: Int): List<Pair<Int, Int>> {
        val rows = mutableListOf<MutableList<Int>>()
        var used = 0
        natural.forEachIndexed { i, w ->
            if (rows.isEmpty() || (used + w > available && rows.last().isNotEmpty())) { rows += mutableListOf<Int>(); used = 0 }
            rows.last() += i; used += w
        }
        val out = MutableList(natural.size) { 0 to 0 }
        rows.forEachIndexed { r, row ->
            val sum = row.sumOf { natural[it] }
            val extra = (available - sum).coerceAtLeast(0)
            row.forEachIndexed { k, i -> out[i] = (minOf(available, natural[i] + extra / row.size + if (k < extra % row.size) 1 else 0)) to r }
        }
        return out
    }
}

private fun TextUnit.times(k: Float): TextUnit = if (isSpecified) this * k else this

/**
 * Text, который при длинном слове (крупный шрифт, узкая строка) уменьшается ровно настолько,
 * чтобы слово не рвалось «Пользовательско/е». Перенос по словам остаётся. Пока размер подбирается,
 * кадр не рисуется, так что разорванное слово не мелькает.
 */
@Composable
fun WordFitText(
    text: String,
    style: TextStyle,
    modifier: Modifier = Modifier,
    color: Color = Color.Unspecified,
    textAlign: TextAlign? = null,
) {
    val fontScale = LocalDensity.current.fontScale
    var scale by remember(text, style, fontScale) { mutableFloatStateOf(1f) }
    var settled by remember(text, style, fontScale) { androidx.compose.runtime.mutableStateOf(false) }
    Text(
        text,
        modifier = modifier.drawWithContent { if (settled) drawContent() },
        color = color,
        textAlign = textAlign,
        style = if (scale == 1f) style else style.copy(fontSize = style.fontSize.times(scale), lineHeight = style.lineHeight.times(scale)),
        onTextLayout = { r ->
            val ends = (0 until r.lineCount).map { r.getLineEnd(it, visibleEnd = false) }
            if (scale > FontFit.MIN_SCALE && FontFit.breaksMidWord(text, ends)) scale = (scale - 0.05f).coerceAtLeast(FontFit.MIN_SCALE)
            else settled = true
        }
    )
}
