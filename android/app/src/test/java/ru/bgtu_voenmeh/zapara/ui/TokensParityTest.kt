package ru.bgtu_voenmeh.zapara.ui

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.theme.*
import java.io.File

class TokensParityTest {
    @Test fun all_colors_match_desktop_argb() {
        val xml = File("../../src/Vograph.Desktop/Theme/Tokens.axaml").readText()
        for ((name, c) in listOf("Dark" to DarkColors, "Light" to LightColors)) {
            val section = xml.substringAfter("<ResourceDictionary x:Key=\"$name\">").substringBefore("</ResourceDictionary>")
            val expected = Regex("x:Key=\"Brush\\.(\\w+)\" Color=\"#([A-Fa-f0-9]+)\"").findAll(section)
                .associate { it.groupValues[1] to it.groupValues[2].let { hex -> (if (hex.length == 6) "FF$hex" else hex).toLong(16).toInt() } }
            val actual = linkedMapOf(
                "Canvas" to c.canvas, "Surface" to c.surface, "Card" to c.card, "CardHover" to c.cardPressed,
                "Chip" to c.chip, "Line" to c.line, "LineStrong" to c.lineStrong,
                "Text1" to c.text1, "Text2" to c.text2, "Text3" to c.text3, "Accent" to c.accent,
                "OnAccent" to c.onAccent, "Selection" to c.selection, "FocusRing" to c.focusRing,
                "Backdrop" to c.backdrop, "SegThumb" to c.segThumb, "Ok" to c.ok,
                "Warn" to c.warn, "WarnSoft" to c.warnSoft, "OnWarn" to c.onWarn,
                "Bad" to c.bad, "BadSoft" to c.badSoft, "OnBad" to c.onBad, "Info" to c.info,
                "MapInk" to c.mapInk, "MapInkSoft" to c.mapInkSoft, "OnMapInk" to c.onMapInk,
                "QrPaper" to c.qrPaper, "CloseHover" to c.closeHover
            )
            c.friends.forEachIndexed { i, color -> actual["Friend${i+1}"] = color }
            assertEquals(expected.keys, actual.keys)
            actual.forEach { (key, color: Color) -> assertEquals("$name.$key", expected[key], color.toArgb()) }
        }
    }

    @Test fun geometry_matches_contract() {
        assertEquals(listOf(4f,8f,12f,16f,24f), listOf(ZaparaSpace.xs.value,ZaparaSpace.s.value,ZaparaSpace.m.value,ZaparaSpace.l.value,ZaparaSpace.xl.value))
        assertEquals(listOf(12f,8f,6f,999f,14f,9f,10f), listOf(ZaparaRadius.card.value,ZaparaRadius.control.value,ZaparaRadius.chip.value,ZaparaRadius.pill.value,ZaparaRadius.dialog.value,ZaparaRadius.icon.value,ZaparaRadius.toast.value))
        assertEquals(44f, ZaparaSpace.minTouch.value)
    }
}
