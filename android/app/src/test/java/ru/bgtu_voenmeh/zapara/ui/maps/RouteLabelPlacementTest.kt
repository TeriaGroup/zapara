package ru.bgtu_voenmeh.zapara.ui.maps

import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.maps.HighlightGeometry.ChipBox

class RouteLabelPlacementTest {
    @Test fun final_clamped_bottom_right_bounds_do_not_overlap() {
        val first = HighlightGeometry.ChipBox(150f, 90f, 150f, 60f)
        val (x, y) = HighlightGeometry.dodge(160f, 120f, 150f, 60f, listOf(first), 300f, 150f, 4f)
        assertFalse("Final clamped rectangles overlap", x < first.x + first.w && x + 150f > first.x &&
            y < first.y + first.h && y + 60f > first.y)
    }

    @Test fun dense_edge_labels_use_final_bounds_and_explicit_no_space() {
        for ((width, height) in listOf(300f to 150f, 320f to 90f, 600f to 140f)) {
            val viewport = ChipBox(0f, 0f, width, height)
            val occupied = mutableListOf(ChipBox(width - 30, height - 30, 24f, 24f))
            repeat(12) {
                val before = occupied.toList()
                val result = RouteLabelPlacement.place(width - 5, height - 5, 150f, 60f, before, viewport, 4f)
                assertEquals(result, RouteLabelPlacement.place(width - 5, height - 5, 150f, 60f, before, viewport, 4f))
                if (result != null) {
                    assertTrue(result.x >= 0 && result.y >= 0 && result.x + result.w <= width && result.y + result.h <= height)
                    assertTrue(before.none { HighlightGeometry.overlaps(result, it, 4f) })
                    occupied += result
                }
            }
            assertNull(RouteLabelPlacement.place(0f, 0f, width + 1, 60f, occupied, viewport, 4f))
        }
    }

    @Test fun short_route_and_room_action_are_reserved_in_large_font_layout() {
        val route = RouteLabelPlacement.routeBounds(listOf(listOf(PathPx(135f, 70f), PathPx(160f, 70f))), 8f)
        val room = ChipBox(0f, 0f, 96f, 96f)
        val occupied = route + room
        val box = RouteLabelPlacement.place(140f, 70f, 160f, 80f, occupied, ChipBox(0f, 0f, 320f, 120f), 4f)
        if (box != null) assertTrue(occupied.none { HighlightGeometry.overlaps(box, it, 4f) })
        assertNull(RouteLabelPlacement.place(0f, 0f, 160f, 80f, listOf(ChipBox(0f, 0f, 320f, 120f)),
            ChipBox(0f, 0f, 320f, 120f), 4f))
    }

    @Test fun large_captions_stay_off_filled_raster_while_compact_numbers_can_sit_near_markers() {
        val raster = ChipBox(0f, 0f, 320f, 200f)
        val viewport = ChipBox(0f, 0f, 320f, 200f)
        assertNull(RouteLabelPlacement.place(40f, 40f, 160f, 80f, listOf(raster), viewport, 4f))
        val compact = requireNotNull(RouteLabelPlacement.place(40f, 40f, 24f, 24f, emptyList(), viewport, 4f))
        assertTrue(compact.x >= 0f && compact.y >= 0f)
        assertTrue(compact.x + compact.w <= viewport.w && compact.y + compact.h <= viewport.h)
    }

    @Test fun large_captions_use_letterbox_not_the_plan() {
        val raster = ChipBox(0f, 20f, 320f, 160f)
        val viewport = ChipBox(0f, 0f, 320f, 200f)
        val box = requireNotNull(RouteLabelPlacement.place(40f, 40f, 120f, 16f, listOf(raster), viewport, 4f))
        assertFalse(HighlightGeometry.overlaps(box, raster, 4f))
        assertTrue(box.y + box.h <= raster.y || box.y >= raster.y + raster.h)
    }

    @Test fun fractional_inverse_zoom_viewport_is_checked_after_rounding() {
        val bounds = ChipBox(18.4f, -10.7f, 92.3f, 80.8f)
        val box = requireNotNull(RouteLabelPlacement.place(110f, 69f, 30f, 20f, emptyList(), bounds, 4f))
        assertTrue(box.x >= bounds.x && box.y >= bounds.y)
        assertTrue(box.x + box.w <= bounds.x + bounds.w && box.y + box.h <= bounds.y + bounds.h)
        assertEquals(box.x.toInt().toFloat(), box.x)
        assertEquals(box.y.toInt().toFloat(), box.y)
    }
}
