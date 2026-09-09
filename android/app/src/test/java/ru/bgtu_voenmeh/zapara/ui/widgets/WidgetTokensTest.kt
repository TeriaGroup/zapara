package ru.bgtu_voenmeh.zapara.ui.widgets

import androidx.compose.ui.graphics.toArgb
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import org.w3c.dom.Element
import ru.bgtu_voenmeh.zapara.ui.theme.DarkColors
import ru.bgtu_voenmeh.zapara.ui.theme.LightColors
import java.io.File
import javax.xml.parsers.DocumentBuilderFactory

class WidgetTokensTest {
    @Test fun widget_colors_match_android_2_tokens() {
        val light = colors(File("src/main/res/values/widget_colors.xml"))
        val night = colors(File("src/main/res/values-night/widget_colors.xml"))
        assertEquals(LightColors.canvas.toArgb(), light.getValue("widget_canvas"))
        assertEquals(LightColors.card.toArgb(), light.getValue("widget_card"))
        assertEquals(LightColors.text1.toArgb(), light.getValue("widget_text1"))
        assertEquals(LightColors.text2.toArgb(), light.getValue("widget_text2"))
        assertEquals(LightColors.warn.toArgb(), light.getValue("widget_warn"))
        assertEquals(LightColors.bad.toArgb(), light.getValue("widget_bad"))
        assertEquals(LightColors.ok.toArgb(), light.getValue("widget_ok"))
        assertEquals(DarkColors.canvas.toArgb(), light.getValue("widget_dark_canvas"))
        assertEquals(LightColors.canvas.toArgb(), light.getValue("widget_light_canvas"))
        assertEquals(DarkColors.canvas.toArgb(), night.getValue("widget_canvas"))
        assertEquals(DarkColors.text1.toArgb(), night.getValue("widget_text1"))
        assertEquals(DarkColors.warn.toArgb(), night.getValue("widget_warn"))
    }

    @Test fun widget_theme_follows_forced_and_system() {
        assertTrue(WidgetTheme.isDark("dark", false))
        assertFalse(WidgetTheme.isDark("light", true))
        assertTrue(WidgetTheme.isDark("system", true))
        assertFalse(WidgetTheme.isDark("system", false))
    }

    @Test fun widget_strings_are_russian_only() {
        val file = File("src/main/res/values/widget_strings.xml")
        val doc = DocumentBuilderFactory.newInstance().newDocumentBuilder().parse(file)
        val nodes = doc.getElementsByTagName("string")
        assertTrue(nodes.length > 0)
        val cyrillic = Regex("[А-Яа-яЁё]")
        val latin = Regex("[A-Za-z]")
        val placeholder = Regex("%\\d+\\$[sd]|%[sd]")
        for (i in 0 until nodes.length) {
            val text = nodes.item(i).textContent
            val stripped = placeholder.replace(text, "")
            assertTrue(text, cyrillic.containsMatchIn(text) || !latin.containsMatchIn(stripped))
        }
    }

    private fun colors(file: File): Map<String, Int> {
        val doc = DocumentBuilderFactory.newInstance().newDocumentBuilder().parse(file)
        val nodes = doc.getElementsByTagName("color")
        return (0 until nodes.length).associate { i ->
            val node = nodes.item(i) as Element
            node.getAttribute("name") to parseColor(node.textContent.trim())
        }
    }

    private fun parseColor(raw: String): Int {
        val hex = raw.removePrefix("#")
        val value = when (hex.length) {
            6 -> "FF$hex"
            8 -> hex
            else -> error(raw)
        }
        return value.toLong(16).toInt()
    }
}
