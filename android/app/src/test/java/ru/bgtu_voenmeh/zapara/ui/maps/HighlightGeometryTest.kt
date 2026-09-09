package ru.bgtu_voenmeh.zapara.ui.maps

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.CoordsRect
import java.io.File
import kotlin.math.abs

class HighlightGeometryTest {
    private val rect = CoordsRect(0.5, 0.25, 0.1, 0.1)

    @Test fun fit_letterbox_then_same_layout_rect_for_canvas_and_chip() {
        val fitted = HighlightGeometry.fit(1000f, 800f, 2000f, 800f)
        assertEquals(0f, fitted.originX, 0.01f)
        assertEquals(200f, fitted.originY, 0.01f)
        assertEquals(1000f, fitted.drawnW, 0.01f)
        assertEquals(400f, fitted.drawnH, 0.01f)
        val local = HighlightGeometry.onLayout(rect, fitted)
        assertEquals(500f, local.left, 0.01f)
        assertEquals(300f, local.top, 0.01f)
        assertEquals(600f, local.right, 0.01f)
        assertEquals(340f, local.bottom, 0.01f)
        val chip = HighlightGeometry.mapped(rect, 1000f, 800f, 2000f, 800f, 1f, 0f, 0f)
        assertEquals(local.left, chip.left, 0.01f)
        assertEquals(local.top, chip.top, 0.01f)
        assertEquals(local.right, chip.right, 0.01f)
        assertEquals(local.bottom, chip.bottom, 0.01f)
    }

    @Test fun center_pivot_scale_matches_graphics_layer() {
        val px = HighlightGeometry.mapped(rect, 1000f, 800f, 1000f, 800f, 2f, 10f, 20f)
        assertEquals(510f, px.left, 0.01f)
        assertEquals(20f, px.top, 0.01f)
        assertEquals(710f, px.right, 0.01f)
        assertEquals(180f, px.bottom, 0.01f)
    }

    @Test fun chip_origin_after_letterbox_and_pinch_is_layout_top_start() {
        val layoutW = 1000f
        val layoutH = 800f
        val mapped = HighlightGeometry.mapped(rect, layoutW, layoutH, 2000f, 800f, 2f, 10f, 20f)
        val gap = 26f
        val chipW = 80f
        val chipH = 24f
        val (ox, oy) = HighlightGeometry.chipOffset(mapped, gap)
        val topStart = HighlightGeometry.offsetVisual(ox, oy, layoutW, layoutH, chipW, chipH, BoxChildOrigin.TopStart)
        assertEquals(mapped.left, topStart.first, 0.01f)
        assertEquals(mapped.top - gap, topStart.second, 0.01f)
        val fromCenter = HighlightGeometry.offsetVisual(ox, oy, layoutW, layoutH, chipW, chipH, BoxChildOrigin.Center)
        assertTrue(
            "wrap-content in a Center box does not share the canvas origin with the rect",
            abs(fromCenter.first - mapped.left) > 1f || abs(fromCenter.second - (mapped.top - gap)) > 1f
        )
        val src = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/maps/ZoomableMap.kt").readText()
        val mark = src.indexOf("Maps.Highlight")
        assertTrue(mark >= 0)
        val chipBlock = src.substring((mark - 500).coerceAtLeast(0), mark)
        assertTrue(
            "Maps.Highlight must align TopStart so Modifier.offset is layout origin, not parent Center",
            chipBlock.contains("Alignment.TopStart")
        )
    }
}
