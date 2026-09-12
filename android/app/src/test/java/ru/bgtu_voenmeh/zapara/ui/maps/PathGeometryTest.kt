package ru.bgtu_voenmeh.zapara.ui.maps

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.CoordsRect
import java.io.File
import kotlin.math.abs

class PathGeometryTest {
    private fun assertPx(expectedX: Float, expectedY: Float, actual: PathPx) {
        assertEquals(expectedX, actual.x, 0.01f)
        assertEquals(expectedY, actual.y, 0.01f)
    }

    @Test fun unit_square_matches_desktop_when_layout_equals_image() {
        val p0 = PathGeometry.mapped(0f, 0f, 100f, 100f, 100f, 100f, 1f, 0f, 0f)
        val p1 = PathGeometry.mapped(1f, 1f, 100f, 100f, 100f, 100f, 1f, 0f, 0f)
        assertPx(0f, 0f, p0)
        assertPx(100f, 100f, p1)
        val stroke = PathGeometry.mapStrokes(
            listOf(listOf(0f to 0f, 1f to 1f)),
            100f, 100f, 100f, 100f, 1f, 0f, 0f
        )
        assertEquals(1, stroke.size)
        assertEquals(2, stroke[0].size)
        assertPx(0f, 0f, stroke[0][0])
        assertPx(100f, 100f, stroke[0][1])
    }

    @Test fun letterbox_matches_highlight_fit() {
        val fitted = HighlightGeometry.fit(1000f, 800f, 2000f, 800f)
        assertEquals(0f, fitted.originX, 0.01f)
        assertEquals(200f, fitted.originY, 0.01f)
        assertEquals(1000f, fitted.drawnW, 0.01f)
        assertEquals(400f, fitted.drawnH, 0.01f)
        val stroke = PathGeometry.mapStrokes(
            listOf(listOf(0f to 0f, 1f to 1f, 0.5f to 0.25f)),
            1000f, 800f, 2000f, 800f, 1f, 0f, 0f
        ).single()
        assertPx(0f, 200f, stroke[0])
        assertPx(1000f, 600f, stroke[1])
        assertPx(500f, 300f, stroke[2])
        assertTrue("letterbox is not image-native pixels", abs(stroke[1].x - 2000f) > 1f)
        assertTrue("letterbox is not a stretched layout square", abs(stroke[1].y - 800f) > 1f)
        val local = PathGeometry.onLayout(0.5f, 0.25f, fitted)
        assertPx(500f, 300f, local)
        val highlight = HighlightGeometry.onLayout(CoordsRect(0.5, 0.25, 0.1, 0.1), fitted)
        assertEquals(highlight.left, local.x, 0.01f)
        assertEquals(highlight.top, local.y, 0.01f)
    }

    @Test fun path_corner_matches_highlight_after_letterbox_and_zoom() {
        val rect = CoordsRect(0.5, 0.25, 0.1, 0.1)
        val highlight = HighlightGeometry.mapped(rect, 1000f, 800f, 2000f, 800f, 2f, 10f, 20f)
        val p = PathGeometry.mapped(0.5f, 0.25f, 1000f, 800f, 2000f, 800f, 2f, 10f, 20f)
        assertEquals(highlight.left, p.x, 0.01f)
        assertEquals(highlight.top, p.y, 0.01f)
    }

    @Test fun center_pivot_scale_matches_highlight() {
        val p = PathGeometry.mapped(0.5f, 0.25f, 1000f, 800f, 1000f, 800f, 2f, 10f, 20f)
        assertPx(510f, 20f, p)
        val highlight = HighlightGeometry.mapped(CoordsRect(0.5, 0.25, 0.1, 0.1), 1000f, 800f, 1000f, 800f, 2f, 10f, 20f)
        assertEquals(highlight.left, p.x, 0.01f)
        assertEquals(highlight.top, p.y, 0.01f)
    }

    @Test fun mapStrokes_keeps_disconnected_floor3_wings() {
        val west = listOf(0.2f to 0.2f, 0.2f to 0.5f)
        val east = listOf(0.8f to 0.5f, 0.8f to 0.2f)
        val strokes = PathGeometry.mapStrokes(listOf(west, east), 100f, 100f, 100f, 100f, 1f, 0f, 0f)
        assertEquals(2, strokes.size)
        assertEquals(2, strokes[0].size)
        assertEquals(2, strokes[1].size)
        assertPx(20f, 20f, strokes[0][0])
        assertPx(20f, 50f, strokes[0][1])
        assertPx(80f, 50f, strokes[1][0])
        assertPx(80f, 20f, strokes[1][1])
        assertTrue(strokes.flatten().none { abs(it.x - 50f) < 0.01f && abs(it.y - 50f) < 0.01f })
    }

    @Test fun mapStrokes_keeps_connected_floor1_corridor() {
        val corridor = listOf(0.2f to 0.5f, 0.5f to 0.5f, 0.8f to 0.5f)
        val strokes = PathGeometry.mapStrokes(listOf(corridor), 100f, 100f, 100f, 100f, 1f, 0f, 0f)
        assertEquals(1, strokes.size)
        assertEquals(3, strokes[0].size)
        assertPx(20f, 50f, strokes[0][0])
        assertPx(50f, 50f, strokes[0][1])
        assertPx(80f, 50f, strokes[0][2])
    }

    @Test fun mapStrokes_empty_null_and_short_are_no_draw() {
        val args = arrayOf(100f, 100f, 100f, 100f, 1f, 0f, 0f)
        fun map(strokes: List<List<Pair<Float, Float>>>?) =
            PathGeometry.mapStrokes(strokes, args[0], args[1], args[2], args[3], args[4], args[5], args[6])
        assertTrue(map(null).isEmpty())
        assertTrue(map(emptyList()).isEmpty())
        assertTrue(map(listOf(listOf(0f to 0f))).isEmpty())
        assertTrue(PathGeometry.mapStrokes(listOf(listOf(0f to 0f, 1f to 1f)), 100f, 100f, 0f, 100f, 1f, 0f, 0f).isEmpty())
        assertTrue(PathGeometry.mapStrokes(listOf(listOf(0f to 0f, 1f to 1f)), 100f, 100f, 100f, 0f, 1f, 0f, 0f).isEmpty())
        val mixed = PathGeometry.mapStrokes(
            listOf(listOf(0f to 0f), listOf(0f to 0f, 1f to 1f), emptyList()),
            100f, 100f, 100f, 100f, 1f, 0f, 0f
        )
        assertEquals(1, mixed.size)
        assertPx(0f, 0f, mixed[0][0])
        assertPx(100f, 100f, mixed[0][1])
    }

    @Test fun zoomable_map_draws_strokes_with_map_ink_after_image() {
        val src = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/maps/ZoomableMap.kt").readText()
        assertTrue("ZoomableMap must take a list of polylines, not one flattened list", Regex("""path:\s*List<List<""").containsMatchIn(src))
        assertTrue(src.contains("= null"))
        assertTrue(src.contains("PathGeometry.mapStrokes"))
        val imageIdx = src.indexOf("Image(")
        val canvasIdx = src.indexOf("Canvas(")
        assertTrue("Canvas must sit after Image", imageIdx >= 0 && canvasIdx > imageIdx)
        assertTrue(src.contains("drawPath"))
        assertTrue(src.contains("c.mapInk"))
        assertTrue(src.contains("drawRoundRect"))
        assertTrue(src.contains("Maps.Highlight"))
        assertTrue(
            "each stroke is a separate path so disconnected wings are not joined",
            src.contains("mappedPath.forEach") || src.contains("for (stroke in mappedPath)")
        )
        assertFalse("must not flatten strokes into one polyline", src.contains("flatten()"))
        assertTrue(
            "empty/null path must not force an extra Canvas",
            src.contains("mappedPath.isNotEmpty()")
        )
        val highlightBlock = src.substring(src.indexOf("drawRoundRect"), src.indexOf("Maps.Highlight"))
        assertFalse("highlight must still draw when path is empty", highlightBlock.contains("mappedPath.isNotEmpty() &&"))
        assertTrue("draw-on uses PathGeometry.at so disconnected wings stay separate", src.contains("PathGeometry.at"))
        assertTrue("traveler and endpoints are circles", src.contains("drawCircle"))
        assertTrue("marching dash after reveal", src.contains("dashPathEffect"))
        assertTrue("path draw duration goes through Durations.map", src.contains("Durations.map"))
        assertTrue("path animation is gated by motion.enabled", src.contains("motion.enabled"))
    }

    private fun px(x: Float, y: Float) = PathPx(x, y)

    @Test fun length_sums_segment_lengths_and_skips_empty() {
        assertEquals(0f, PathGeometry.length(emptyList()), 0.001f)
        assertEquals(0f, PathGeometry.length(listOf(px(1f, 1f))), 0.001f)
        assertEquals(7f, PathGeometry.length(listOf(px(0f, 0f), px(3f, 0f), px(3f, 4f))), 0.001f)
        assertEquals(7f, PathGeometry.strokesLength(listOf(
            listOf(px(0f, 0f), px(3f, 0f)),
            listOf(px(8f, 0f), px(12f, 0f))
        )), 0.001f)
    }

    @Test fun prefix_cuts_on_a_vertex_and_inside_a_segment() {
        val l = listOf(px(0f, 0f), px(3f, 0f), px(3f, 4f))
        assertEquals(listOf(px(0f, 0f)), PathGeometry.prefix(l, 0f))
        assertPx(1.5f, 0f, PathGeometry.prefix(l, 1.5f)[1])
        assertEquals(2, PathGeometry.prefix(l, 3f).size)
        assertPx(3f, 2f, PathGeometry.prefix(l, 5f).last())
        assertEquals(3, PathGeometry.prefix(l, 100f).size)
        assertTrue(PathGeometry.prefix(emptyList(), 1f).isEmpty())
    }

    @Test fun at_reveals_disconnected_strokes_in_order() {
        val strokes = listOf(
            listOf(px(0f, 0f), px(2f, 0f)),
            listOf(px(5f, 0f), px(7f, 0f))
        )
        val start = PathGeometry.at(strokes, 0f)
        assertTrue(start.revealed.isEmpty())
        assertFalse(start.complete)
        assertPx(0f, 0f, start.head!!)
        assertPx(0f, 0f, start.start!!)
        assertPx(7f, 0f, start.end!!)

        val half = PathGeometry.at(strokes, 0.5f)
        assertEquals(1, half.revealed.size)
        assertPx(2f, 0f, half.head!!)
        assertFalse(half.complete)

        val three = PathGeometry.at(strokes, 0.75f)
        assertEquals(2, three.revealed.size)
        assertPx(6f, 0f, three.head!!)

        val done = PathGeometry.at(strokes, 1f)
        assertTrue(done.complete)
        assertEquals(2, done.revealed.size)
        assertPx(7f, 0f, done.head!!)
    }

    @Test fun at_empty_is_complete_with_nothing_to_draw() {
        val empty = PathGeometry.at(emptyList(), 0.4f)
        assertTrue(empty.revealed.isEmpty())
        assertTrue(empty.complete)
        assertNull(empty.head)
    }

    @Test fun drawProgress_clamps_and_snaps_without_duration() {
        assertEquals(0f, PathGeometry.drawProgress(0f, 1200), 0.001f)
        assertEquals(0.5f, PathGeometry.drawProgress(600f, 1200), 0.001f)
        assertEquals(1f, PathGeometry.drawProgress(1200f, 1200), 0.001f)
        assertEquals(1f, PathGeometry.drawProgress(50f, 0), 0.001f)
        assertEquals(0f, PathGeometry.drawProgress(-20f, 1200), 0.001f)
    }

    @Test fun loopProgress_sits_at_end_until_draw_finishes() {
        assertEquals(1f, PathGeometry.loopProgress(0f, 1200, 800), 0.001f)
        assertEquals(0f, PathGeometry.loopProgress(1200f, 1200, 800), 0.001f)
        assertEquals(0.5f, PathGeometry.loopProgress(1600f, 1200, 800), 0.001f)
        assertEquals(1f, PathGeometry.loopProgress(500f, 0, 800), 0.001f)
    }

    @Test fun dashOffset_marches_a_period() {
        assertEquals(0f, PathGeometry.dashOffset(0f, 1000, 18f), 0.001f)
        assertEquals(9f, PathGeometry.dashOffset(500f, 1000, 18f), 0.001f)
        assertEquals(0f, PathGeometry.dashOffset(250f, 0, 18f), 0.001f)
    }
}
