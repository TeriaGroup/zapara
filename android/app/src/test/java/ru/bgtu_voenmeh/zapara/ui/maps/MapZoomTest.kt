package ru.bgtu_voenmeh.zapara.ui.maps

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class MapZoomTest {
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
}
