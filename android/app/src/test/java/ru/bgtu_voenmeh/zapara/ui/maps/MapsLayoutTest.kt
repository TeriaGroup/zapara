package ru.bgtu_voenmeh.zapara.ui.maps

import java.io.File
import org.junit.Assert.*
import org.junit.Test

class MapsLayoutTest {
    @Test fun landscape_short_side_puts_chrome_beside_the_plan() {
        assertTrue(MapsLayout.compact(923, 411))
        assertFalse(MapsLayout.compact(411, 923))
        assertFalse(MapsLayout.compact(411, 700))
    }

    @Test fun large_font_and_short_windows_have_bounded_collapsed_chrome() {
        for (width in listOf(320, 360, 600)) for (scale in listOf(1f, 1.5f, 2f)) {
            assertEquals(width < 360 || scale >= 1.5f, MapsLayout.collapsed(width, 800, scale))
            assertTrue(MapsLayout.collapsed(width, 320, scale))
        }
        assertTrue(MapsLayout.ChromeFraction + MapsLayout.StepsFraction < 1f)
        assertEquals(200, MapsLayout.chromeMaxDp(800))
        assertEquals(250, MapsLayout.chromeMaxDp(1000))
        assertEquals(40, MapsLayout.chromeMaxDp(200))
        assertEquals(640, MapsLayout.chromeMaxDp(800, 2f))
        assertEquals(0, MapsLayout.chromeMaxDp(100))
        assertEquals(0, MapsLayout.chromeMaxDp(160))
        assertTrue(MapsLayout.compactSteps(411, 923))
        assertTrue(MapsLayout.compactSteps(923, 411))
        assertFalse(MapsLayout.compactSteps(923, 700))
    }

    @Test fun opaque_zoom_bar_is_shared_outside_raster_and_keeps_all_controls() {
        val section = source("MapsSection")
        val bar = section.substring(section.indexOf("internal fun MapsZoomRow"), section.indexOf("private fun RouteEnd"))
        assertTrue(bar.contains("ZCard(tag = \"Maps.ZoomBar\""))
        assertTrue(bar.contains("FlowRow"))
        listOf("Maps.ZoomIn", "Maps.ZoomOut", "Maps.Fit", "Maps.Fullscreen").forEach { assertTrue(bar.contains(it)) }
        assertFalse(bar.contains("Color.Transparent"))
        val fullscreen = source("MapFullscreen")
        assertTrue(fullscreen.contains("\"Maps.Close\""))
        assertTrue(fullscreen.indexOf("FullscreenClose(onEvent)") < fullscreen.indexOf("MapsPlanPane("))
        assertTrue(fullscreen.contains("MapsLayout.compact(maxWidth.value.toInt(), maxHeight.value.toInt())"))
        assertTrue(fullscreen.contains("systemBarsPadding()"))
        assertTrue(fullscreen.contains(".fillMaxHeight().verticalScroll(rememberScrollState())"))
        assertTrue(fullscreen.contains("MapsZoomRow(onEvent, showFullscreen = false)"))
        assertTrue(fullscreen.contains("Modifier.weight(1f).fillMaxHeight(), compact = true"))
        assertFalse(fullscreen.contains("ZoomableMap("))
        assertTrue(section.contains("heightIn(min = minStep, max = maxOf(stepHeight, minStep))"))
        assertTrue(section.contains("heightIn(max = chromeMax).verticalScroll"))
        assertTrue(section.contains("MapsLayout.chromeMaxDp(maxHeight.value.roundToInt(), fontScale)"))
        assertTrue(section.contains("MapsLayout.compactSteps("))
        assertTrue(source("RouteStepBar").contains("compact: Boolean"))
    }

    @Test fun new_path_uses_presentation_and_one_common_image_layer() {
        val map = source("ZoomableMap")
        assertTrue(map.contains("if (presentation == null) path else null"))
        assertTrue(map.contains("if (presentation == null) stairMarkers else emptyList()"))
        assertTrue(map.contains("RouteMapOverlay(presentation, floorKey, activeStepId"))
        assertEquals(1, Regex("graphicsLayer\\(scaleX = shown").findAll(map).count())
        assertTrue(map.contains("HighlightGeometry.fromLayout"))
        assertTrue(map.contains("unavailableCallback?.invoke()"))
        assertFalse(source("MapsSection").contains("MapsEvent.PickRouteStep"))
        assertFalse(source("MapsSection").contains("TextOverflow.Ellipsis"))
    }

    private fun source(name: String) = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/maps/$name.kt").readText()
}
