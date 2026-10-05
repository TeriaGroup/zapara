package ru.bgtu_voenmeh.zapara.ui

import androidx.compose.ui.graphics.Color
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.theme.DarkColors
import ru.bgtu_voenmeh.zapara.ui.theme.LightColors
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaColors
import kotlin.math.pow

class AccessibilityContrastTest {
    @Test fun normal_text_on_all_surfaces_meets_aa() {
        assertSurfacePairs(4.5) { c ->
            mapOf("text1" to c.text1, "text2" to c.text2, "text3" to c.text3)
        }
    }

    @Test fun meaningful_boundaries_on_all_surfaces_meet_aa() {
        assertSurfacePairs(3.0) { c ->
            mapOf("lineStrong" to c.lineStrong, "focusRing" to c.focusRing)
        }
    }

    @Test fun on_accent_meets_normal_text_aa() {
        for ((name, c) in themes) {
            assertTrue("$name.onAccent/accent", contrast(c.onAccent, c.accent) >= 4.5)
        }
    }

    @Test fun contrast_composites_alpha_before_linearizing_srgb() {
        assertEquals(21.0, contrast(Color.Black, Color.White), 0.000001)
        assertEquals(1.0, contrast(Color.Transparent, Color.White), 0.000001)
        val translucentBlack = Color.Black.copy(alpha = 0.5f)
        val channel = 1.0 - translucentBlack.alpha.toDouble()
        val expected = 1.05 / (((channel + 0.055) / 1.055).pow(2.4) + 0.05)
        assertEquals(expected, contrast(translucentBlack, Color.White), 0.000001)
        // The low-channel linear branch must also be exercised.
        val lowLuminance = (0.2126 + 0.7152 * 2 + 0.0722 * 3) / (255 * 12.92)
        assertEquals((lowLuminance + 0.05) / 0.05, contrast(Color(0xFF010203), Color.Black), 0.000001)
    }

    private val themes = listOf("Dark" to DarkColors, "Light" to LightColors)

    private fun assertSurfacePairs(minimum: Double, foregrounds: (ZaparaColors) -> Map<String, Color>) {
        val failures = mutableListOf<String>()
        for ((theme, c) in themes) {
            val surfaces = mapOf(
                "canvas" to c.canvas, "surface" to c.surface, "card" to c.card,
                "cardPressed" to c.cardPressed, "chip" to c.chip, "segThumb" to c.segThumb
            )
            for ((fgName, fg) in foregrounds(c)) {
                for ((bgName, bg) in surfaces) {
                    val ratio = contrast(fg, bg)
                    if (ratio < minimum) failures += "$theme.$fgName/$bgName = $ratio < $minimum"
                }
            }
        }
        assertTrue(failures.joinToString("\n"), failures.isEmpty())
    }

    private fun contrast(fg: Color, bg: Color): Double {
        require(bg.alpha == 1f) { "Contrast backgrounds must be opaque surfaces" }
        fun linear(c: Double) = if (c <= 0.04045) c / 12.92 else ((c + 0.055) / 1.055).pow(2.4)
        fun luminance(r: Double, g: Double, b: Double) =
            0.2126 * linear(r) + 0.7152 * linear(g) + 0.0722 * linear(b)
        val alpha = fg.alpha.toDouble()
        fun composite(front: Float, back: Float) = alpha * front + (1.0 - alpha) * back
        val front = luminance(composite(fg.red, bg.red), composite(fg.green, bg.green), composite(fg.blue, bg.blue))
        val back = luminance(bg.red.toDouble(), bg.green.toDouble(), bg.blue.toDouble())
        return (maxOf(front, back) + 0.05) / (minOf(front, back) + 0.05)
    }
}
