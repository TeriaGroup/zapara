package ru.bgtu_voenmeh.zapara.ui.maps

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.CoordsRect
import java.io.File

/** #103 (AN-03): аудитория пары выделена, карта приближена и отцентрирована на ней. */
class MapFocusTest {
    private val vw = 358f; private val vh = 420f; private val iw = 2000f; private val ih = 1400f

    private fun centerOnScreen(rect: CoordsRect): Pair<Float, Float> {
        val f = MapFocus.focus(rect)
        val (ox, oy) = MapZoom.restoredPan(f.panX, f.panY, vw, vh, iw, ih, f.zoom)
        val px = HighlightGeometry.mapped(rect, vw, vh, iw, ih, f.zoom, ox, oy)
        return (px.left + px.right) / 2f to (px.top + px.bottom) / 2f
    }

    @Test fun room_in_the_middle_of_the_plan_lands_in_the_middle_of_the_view() {
        val rect = CoordsRect(0.45, 0.48, 0.055, 0.042)
        val (x, y) = centerOnScreen(rect)
        assertEquals(vw / 2f, x, 1f)
        assertEquals(vh / 2f, y, 1f)
    }

    @Test fun zoom_makes_a_typical_room_larger_but_within_limits() {
        val f = MapFocus.focus(CoordsRect(0.4, 0.4, 0.055, 0.042))
        assertTrue(f.zoom in MapFocus.MIN_ZOOM..MapFocus.MAX_ZOOM)
        assertTrue(f.zoom <= MapZoom.Max)
        assertEquals(MapFocus.MIN_ZOOM, MapFocus.focus(CoordsRect(0.1, 0.1, 0.5, 0.5)).zoom, 0.001f)
    }

    @Test fun room_at_the_edge_stays_visible_after_clamping() {
        val rect = CoordsRect(0.94, 0.9, 0.055, 0.042)
        val f = MapFocus.focus(rect)
        val (ox, oy) = MapZoom.restoredPan(f.panX, f.panY, vw, vh, iw, ih, f.zoom)
        val px = HighlightGeometry.mapped(rect, vw, vh, iw, ih, f.zoom, ox, oy)
        assertTrue("комната в окне: $px", px.left >= -1f && px.right <= vw + 1f && px.top >= -1f && px.bottom <= vh + 1f)
    }

    @Test fun only_for_the_lesson_room_and_not_over_a_route() {
        val r = CoordsRect(0.4, 0.4, 0.05, 0.05)
        assertTrue(MapFocus.applies(MapMode.Lesson, false, r))
        assertTrue(MapFocus.applies(MapMode.NextLesson, false, r))
        assertFalse("ручной просмотр", MapFocus.applies(MapMode.Manual, false, r))
        assertFalse("выбран шаг маршрута", MapFocus.applies(MapMode.Lesson, true, r))
        assertFalse("нет на плане", MapFocus.applies(MapMode.Lesson, false, null))
    }

    @Test fun view_model_uses_focus_and_names_a_missing_room() {
        val vm = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/maps/MapsViewModel.kt").readText()
        assertTrue(vm.contains("MapFocus.applies(mode, selected != null, coords)"))
        assertTrue(vm.contains("unmarkedRoom = if (!room.isNullOrBlank() && coords == null) room else \"\""))
        assertTrue(File("src/main/res/values/strings_map_focus.xml").readText()
            .contains("Аудитории %1\$s нет на плане этого этажа"))
    }

    @Test fun web_auto_zoom_stays_off() {
        val web = File("../../web/src/map-labels.ts")
        if (web.exists()) assertTrue("web: autoZoomNextRoom = false",
            web.readText().contains("export const autoZoomNextRoom = false;"))
    }
}
