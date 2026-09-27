package ru.bgtu_voenmeh.zapara.ui.maps

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class MapZoomTest {
    @Test fun wide_fitted_plan_stays_centered_in_tall_viewport() {
        assertEquals(0f to 0f, MapZoom.clampPan(900f, -900f, 400f, 800f, 1600f, 400f, 1f))
    }

    @Test fun zoomed_wide_plan_pans_only_along_overflowing_axis() {
        assertEquals(200f to 0f, MapZoom.clampPan(900f, -900f, 400f, 800f, 1600f, 400f, 2f))
        assertEquals(-200f to 0f, MapZoom.clampPan(-900f, 900f, 400f, 800f, 1600f, 400f, 2f))
    }

    @Test fun both_overflow_axes_clamp_at_image_edges() {
        assertEquals(400f to -400f, MapZoom.clampPan(900f, -900f, 400f, 400f, 1000f, 1000f, 3f))
        assertEquals(75f to -90f, MapZoom.clampPan(75f, -90f, 400f, 400f, 1000f, 1000f, 3f))
    }

    @Test fun shrinking_zoom_reclamps_previous_pan() {
        val large = MapZoom.clampPan(900f, -900f, 400f, 400f, 1000f, 1000f, 4f)
        assertEquals(100f to -100f, MapZoom.clampPan(large.first, large.second, 400f, 400f, 1000f, 1000f, 1.5f))
        assertEquals(0f to 0f, MapZoom.clampPan(large.first, large.second, 400f, 400f, 1000f, 1000f, MapZoom.Min))
    }

    @Test fun missing_image_or_viewport_cannot_pan() {
        assertEquals(0f to 0f, MapZoom.clampPan(100f, 100f, 0f, 800f, 1600f, 400f, 2f))
        assertEquals(0f to 0f, MapZoom.clampPan(100f, 100f, 400f, 800f, 0f, 0f, 2f))
    }

    @Test fun pinch_uses_local_scale() {
        assertEquals(1.6f, MapZoom.shown(gesture = true, pinchScale = 1.6f, animatedZoom = 1f), 0.001f)
    }

    @Test fun buttons_use_animated_zoom_when_not_pinching() {
        assertEquals(2f, MapZoom.shown(gesture = false, pinchScale = 1.6f, animatedZoom = 2f), 0.001f)
    }

    @Test fun pinch_echo_is_not_a_button() {
        assertFalse(MapZoom.isButtonZoom(zoom = 1.6f, lastEmitted = 1.6f))
    }

    @Test fun zoom_in_after_pinch_is_a_button() {
        val pinched = 1.6f
        val fromButton = (pinched * 1.25f).coerceIn(MapZoom.Min, MapZoom.Max)
        assertTrue(MapZoom.isButtonZoom(fromButton, pinched))
        val gesture = false
        assertEquals(fromButton, MapZoom.shown(gesture, pinched, fromButton), 0.001f)
    }

    @Test fun size_change_after_rotation_resets_pan() {
        assertFalse(MapZoom.shouldResetView(0, 0, 1080, 800))
        assertFalse(MapZoom.shouldResetView(1080, 800, 1080, 800))
        assertTrue(MapZoom.shouldResetView(1800, 400, 1080, 800))
        assertTrue(MapZoom.shouldResetView(1080, 800, 1800, 400))
        val src = java.io.File("src/main/java/ru/bgtu_voenmeh/zapara/ui/maps/ZoomableMap.kt").readText()
        assertTrue(src.contains("shouldResetView"))
    }
}
