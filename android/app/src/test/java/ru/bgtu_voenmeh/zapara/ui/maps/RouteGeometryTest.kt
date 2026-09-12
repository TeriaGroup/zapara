package ru.bgtu_voenmeh.zapara.ui.maps

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.campus.GraphPoint
import ru.bgtu_voenmeh.zapara.data.campus.Leg
import ru.bgtu_voenmeh.zapara.data.campus.Route

class RouteGeometryTest {
    private val floor = FloorKey("ГК", 1)
    private val size = RasterSize(1000, 500)
    private fun segment(vararg points: GraphPoint) = RouteSegment(7, floor, RoutePartKind.Walk, points.toList())
    private fun assertArrow(x: Float, y: Float, ux: Float, uy: Float, arrow: RouteArrow) {
        assertEquals(x, arrow.tip.x, 1e-4f)
        assertEquals(y, arrow.tip.y, 1e-4f)
        assertEquals(ux, arrow.unitX, 1e-4f)
        assertEquals(uy, arrow.unitY, 1e-4f)
        assertEquals(1f, arrow.unitX * arrow.unitX + arrow.unitY * arrow.unitY, 1e-4f)
    }
    private fun assertEmpty(raster: RasterSegment) {
        assertTrue(raster.points.isEmpty())
        assertTrue(raster.arrows.isEmpty())
        assertNull(raster.bounds)
        assertEquals(7, raster.legIndex)
        assertEquals(floor, raster.floor)
    }

    @Test fun normalized_points_have_raster_units_and_preserve_identity() {
        val raster = RouteGeometry.toRaster(segment(GraphPoint(0.25, 0.5), GraphPoint(0.75, 0.5)), size)
        assertEquals(listOf(PathPx(250f, 250f), PathPx(750f, 250f)), raster.points)
        assertEquals(RouteBounds(250f, 250f, 750f, 250f), raster.bounds)
        assertEquals(7, raster.legIndex)
        assertEquals(floor, raster.floor)
        assertEquals(7, raster.arrows.size)
        raster.arrows.forEachIndexed { i, arrow -> assertArrow(250f + (i + 1) * 64f, 250f, 1f, 0f, arrow) }
    }

    @Test fun inclusive_edges_and_interior_extrema_define_bounds() {
        val raster = RouteGeometry.toRaster(segment(GraphPoint(0.5, 0.5), GraphPoint(1.0, 0.0),
            GraphPoint(0.0, 1.0), GraphPoint(0.75, 0.5)), size)
        assertEquals(PathPx(1000f, 0f), raster.points[1])
        assertEquals(PathPx(0f, 500f), raster.points[2])
        assertEquals(RouteBounds(0f, 0f, 1000f, 500f), raster.bounds)
    }

    @Test fun invalid_normalized_point_rejects_whole_stroke_without_clamping_or_bridging() {
        for (bad in listOf(Double.NaN, Double.POSITIVE_INFINITY, Double.NEGATIVE_INFINITY, -0.001, 1.001)) {
            for (point in listOf(GraphPoint(bad, 0.5), GraphPoint(0.5, bad))) {
                assertEmpty(RouteGeometry.toRaster(segment(GraphPoint(0.0, 0.0), point, GraphPoint(1.0, 1.0)), size))
            }
        }
    }

    @Test fun empty_and_explicitly_invalid_segments_are_nondrawable() {
        assertEmpty(RouteGeometry.toRaster(segment(), size))
        for (problem in listOf(RouteProblem.InvalidGeometry, RouteProblem.MissingCoordinates, RouteProblem.UnsupportedLeg)) {
            assertEmpty(RouteGeometry.toRaster(segment(GraphPoint(0.0, 0.0), GraphPoint(1.0, 1.0))
                .copy(problems = setOf(problem, RouteProblem.MissingMap)), size))
        }
    }

    @Test fun missing_map_alone_does_not_erase_valid_geometry_with_supplied_raster_size() {
        val source = segment(GraphPoint(0.0, 0.0), GraphPoint(1.0, 0.0))
        assertEquals(RouteGeometry.toRaster(source, size),
            RouteGeometry.toRaster(source.copy(problems = setOf(RouteProblem.MissingMap)), size))
    }

    @Test fun stairs_never_become_flat_strokes_but_local_building_link_keeps_authored_points() {
        val source = segment(GraphPoint(0.0, 0.0), GraphPoint(1.0, 0.0))
        for (kind in listOf(RoutePartKind.StairUp, RoutePartKind.StairDown)) {
            assertEmpty(RouteGeometry.toRaster(source.copy(kind = kind), size))
        }
        assertEquals(RouteGeometry.toRaster(source, size),
            RouteGeometry.toRaster(source.copy(kind = RoutePartKind.BuildingLink), size))
    }

    @Test fun singleton_and_zero_length_keep_point_bounds_without_arrows() {
        for (points in listOf(listOf(GraphPoint(0.25, 0.5)), List(3) { GraphPoint(0.25, 0.5) })) {
            val raster = RouteGeometry.toRaster(segment(*points.toTypedArray()), size)
            assertEquals(List(points.size) { PathPx(250f, 250f) }, raster.points)
            assertEquals(RouteBounds(250f, 250f, 250f, 250f), raster.bounds)
            assertTrue(raster.arrows.isEmpty())
        }
        assertNull(RouteGeometry.bounds(emptyList()))
        assertTrue(RouteGeometry.arrows(emptyList(), 64f).isEmpty())
    }

    @Test fun nonfinite_pixel_inputs_reject_whole_bounds_and_arrow_sampling() {
        for (bad in listOf(Float.NaN, Float.POSITIVE_INFINITY, Float.NEGATIVE_INFINITY)) {
            for (point in listOf(PathPx(bad, 0f), PathPx(0f, bad))) {
                val points = listOf(PathPx(0f, 0f), point, PathPx(200f, 0f))
                assertNull(RouteGeometry.bounds(points))
                assertTrue(RouteGeometry.arrows(points, 64f).isEmpty())
            }
        }
    }

    @Test fun spacing_must_be_finite_and_positive_even_for_empty_input() {
        for (spacing in listOf(0f, -1f, Float.NaN, Float.POSITIVE_INFINITY, Float.NEGATIVE_INFINITY)) {
            for (points in listOf(emptyList(), listOf(PathPx(0f, 0f), PathPx(100f, 0f)))) {
                assertThrows(IllegalArgumentException::class.java) { RouteGeometry.arrows(points, spacing) }
            }
        }
    }

    @Test fun regular_sampling_includes_exact_end_but_not_an_extra_tail_arrow() {
        val arrows = RouteGeometry.arrows(listOf(PathPx(0f, 0f), PathPx(192f, 0f)), 64f)
        assertEquals(3, arrows.size)
        arrows.forEachIndexed { i, a -> assertArrow((i + 1) * 64f, 0f, 1f, 0f, a) }
        assertEquals(arrows, RouteGeometry.arrows(listOf(PathPx(0f, 0f), PathPx(200f, 0f)), 64f))
    }

    @Test fun distance_continues_across_corner_and_tangents_follow_each_arm() {
        val arrows = RouteGeometry.arrows(listOf(PathPx(0f, 0f), PathPx(100f, 0f), PathPx(100f, 100f)), 64f)
        assertEquals(3, arrows.size)
        assertArrow(64f, 0f, 1f, 0f, arrows[0])
        assertArrow(100f, 28f, 0f, 1f, arrows[1])
        assertArrow(100f, 92f, 0f, 1f, arrows[2])
    }

    @Test fun exact_corner_uses_incoming_nonzero_edge_tangent() {
        val arrows = RouteGeometry.arrows(listOf(PathPx(0f, 0f), PathPx(64f, 0f), PathPx(64f, 64f)), 64f)
        assertEquals(2, arrows.size)
        assertArrow(64f, 0f, 1f, 0f, arrows[0])
        assertArrow(64f, 64f, 0f, 1f, arrows[1])
    }

    @Test fun duplicate_points_do_not_change_spacing_or_corner_tangent() {
        val points = listOf(PathPx(0f, 0f), PathPx(64f, 0f), PathPx(64f, 64f))
        assertEquals(RouteGeometry.arrows(points, 32f), RouteGeometry.arrows(points.flatMap { listOf(it, it, it) }, 32f))
    }

    @Test fun short_bent_path_uses_arc_midpoint_not_endpoint_average() {
        val arrows = RouteGeometry.arrows(listOf(PathPx(0f, 0f), PathPx(12f, 0f), PathPx(12f, 28f)), 64f)
        assertEquals(1, arrows.size)
        assertArrow(12f, 8f, 0f, 1f, arrows.single())
    }

    @Test fun reversed_short_path_keeps_midpoint_and_reverses_tangent() {
        val points = listOf(PathPx(0f, 0f), PathPx(12f, 16f))
        assertArrow(6f, 8f, 0.6f, 0.8f, RouteGeometry.arrows(points, 64f).single())
        assertArrow(6f, 8f, -0.6f, -0.8f, RouteGeometry.arrows(points.reversed(), 64f).single())
    }

    @Test fun reversed_long_corner_path_samples_from_new_start() {
        val arrows = RouteGeometry.arrows(listOf(PathPx(100f, 100f), PathPx(100f, 0f), PathPx(0f, 0f)), 64f)
        assertEquals(3, arrows.size)
        assertArrow(100f, 36f, 0f, -1f, arrows[0])
        assertArrow(72f, 0f, -1f, 0f, arrows[1])
        assertArrow(8f, 0f, -1f, 0f, arrows[2])
    }

    @Test fun disconnected_strokes_reset_sampling_and_never_bridge_even_on_same_floor() {
        val next = FloorKey("УЛК", 2)
        val route = Route(10, listOf(
            Leg("walk", floor.building, floor.floor, null, null, listOf(GraphPoint(0.0, 0.0), GraphPoint(0.04, 0.0))),
            Leg("walk", floor.building, floor.floor, null, null, listOf(GraphPoint(0.8, 0.0), GraphPoint(0.9, 0.0))),
            Leg("building_link", floor.building, floor.floor, next.building, next.floor,
                listOf(GraphPoint(0.9, 0.0), GraphPoint(0.1, 0.5))),
            Leg("walk", next.building, next.floor, null, null, listOf(GraphPoint(0.1, 0.5), GraphPoint(0.14, 0.5)))
        ))
        val presentation = RoutePresentationBuilder.build(route, null, null, mapOf(floor to size, next to size))
        val rasters = presentation.segments.map { RouteGeometry.toRaster(it, size) }
        assertEquals(listOf(0, 1, 3), rasters.map { it.legIndex })
        assertEquals(listOf(floor, floor, next), rasters.map { it.floor })
        assertEquals(listOf(2, 2, 2), rasters.map { it.points.size })
        assertArrow(20f, 0f, 1f, 0f, rasters[0].arrows.single())
        assertArrow(864f, 0f, 1f, 0f, rasters[1].arrows.single())
        assertArrow(120f, 250f, 1f, 0f, rasters[2].arrows.single())
    }

    @Test fun letterbox_and_negative_pan_use_existing_fit_once_in_separate_layout_units() {
        val raster = RouteGeometry.toRaster(segment(GraphPoint(0.0, 0.0), GraphPoint(1.0, 1.0)), size)
        val fit = HighlightGeometry.fit(300f, 300f, 1000f, 500f)
        assertEquals(FittedImage(0f, 75f, 300f, 150f), fit)
        val layout = raster.points.map { PathGeometry.onLayout(it.x / size.width, it.y / size.height, fit) }
        assertEquals(listOf(PathPx(0f, 75f), PathPx(300f, 225f)), layout)
        assertEquals(RouteBounds(0f, 75f, 300f, 225f), RouteGeometry.bounds(layout))
        val panned = layout.map { PathGeometry.afterLayer(it, 2f, -50f, -100f, 150f, 150f) }
        assertEquals(RouteBounds(-200f, -100f, 400f, 200f), RouteGeometry.bounds(panned))
        assertEquals(PathGeometry.mapped(1f, 1f, 300f, 300f, 1000f, 500f, 2f, -50f, -100f), panned.last())
        assertEquals(RouteBounds(0f, 0f, 1000f, 500f), raster.bounds)
    }

    @Test fun tiny_nonzero_edges_are_not_discarded_by_an_epsilon() {
        val arrows = RouteGeometry.arrows(listOf(PathPx(0f, 0f), PathPx(1e-7f, 0f)), 64f)
        assertEquals(1, arrows.size)
        assertEquals(5e-8f, arrows.single().tip.x, 1e-12f)
        assertArrow(5e-8f, 0f, 1f, 0f, arrows.single())
    }

    @Test fun large_finite_pixel_edges_do_not_overflow_float_length_math() {
        val arrows = RouteGeometry.arrows(listOf(PathPx(-Float.MAX_VALUE, 0f), PathPx(Float.MAX_VALUE, 0f)), Float.MAX_VALUE)
        assertEquals(2, arrows.size)
        assertArrow(0f, 0f, 1f, 0f, arrows[0])
        assertArrow(Float.MAX_VALUE, 0f, 1f, 0f, arrows[1])
    }

    @Test fun unrepresentable_arrow_count_returns_empty_without_looping_or_allocating() {
        assertTrue(RouteGeometry.arrows(listOf(PathPx(0f, 0f), PathPx(100f, 0f)), Float.MIN_VALUE).isEmpty())
    }
}
