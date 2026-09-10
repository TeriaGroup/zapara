package ru.bgtu_voenmeh.zapara.ui.maps

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.campus.GraphPoint
import ru.bgtu_voenmeh.zapara.data.campus.Leg
import ru.bgtu_voenmeh.zapara.data.campus.Node
import ru.bgtu_voenmeh.zapara.data.campus.Route

class RoutePresentationTest {
    private val g1 = FloorKey("ГК", 1)
    private val g2 = FloorKey("ГК", 2)
    private val u2 = FloorKey("УЛК", 2)
    private val a = GraphPoint(0.0, 0.0)
    private val b = GraphPoint(0.3, 0.2)
    private val c = GraphPoint(0.8, 1.0)
    private val maps = listOf(g1, g2, u2).associateWith { RasterSize(1000, 500) }

    private fun leg(kind: String = "walk", floor: FloorKey = g1, to: FloorKey? = null,
        points: List<GraphPoint> = listOf(a, b)) =
        Leg(kind, floor.building, floor.floor, to?.building, to?.floor, points)

    private fun node(id: String, floor: FloorKey = g1, point: GraphPoint = a) =
        Node(id, "junction", floor.building, floor.floor, point.x, point.y)

    private fun build(vararg legs: Leg) = RoutePresentationBuilder.build(
        Route(40, legs.toList()), node("start"), node("end", u2, c), maps)

    @Test fun ordered_parts_keep_leg_step_ids_and_separate_segment_indices() {
        val route = Route(40, listOf(leg(), leg(points = listOf(c, b)),
            leg("stair_up", g1, g2), leg(floor = g2),
            leg("building_link", g2, u2), leg(floor = u2)))
        val p = RoutePresentationBuilder.build(route, node("s"), node("d", u2, c), maps)
        assertEquals(listOf(0, 2, 3, 4, 5), p.steps.map { it.id })
        assertEquals(listOf(RoutePartKind.Walk, RoutePartKind.StairUp, RoutePartKind.Walk,
            RoutePartKind.BuildingLink, RoutePartKind.Walk), p.steps.map { it.kind })
        assertEquals(listOf(0, 1, 3, 5), p.segments.map { it.legIndex })
        assertEquals(listOf(listOf(0, 1), emptyList(), listOf(2), emptyList(), listOf(3)),
            p.steps.map { it.segmentIndices })
        assertEquals(listOf(listOf(a, b), listOf(c, b), listOf(a, b), listOf(a, b)),
            p.segments.map { it.points })
        assertEquals(listOf(g1, g1, g2, u2), p.segments.map { it.floor })
        assertEquals(p, RoutePresentationBuilder.build(route, node("s"), node("d", u2, c), maps))
        assertFalse(p.arrived)
        assertTrue(p.problems.isEmpty())
    }

    @Test fun return_to_floor_keeps_distinct_step_ids() {
        val p = build(leg(), leg("stair_up", g1, g2), leg("stair_down", g2, g1), leg())
        assertEquals(listOf(0, 1, 2, 3), p.steps.map { it.id })
        assertEquals(p.steps.first().from, p.steps.last().from)
        assertEquals(2, p.segments.size)
    }

    @Test fun adjacent_walks_on_different_plans_never_group() {
        assertEquals(listOf(0, 1, 2), build(leg(), leg(floor = g2), leg(floor = u2)).steps.map { it.id })
    }

    @Test fun transitions_have_local_markers_not_flat_lines() {
        for (kind in listOf("stair_up", "stair_down", "building_link")) {
            val target = if (kind == "building_link") u2 else g2
            val p = build(leg(kind, g1, target, listOf(b, c)))
            val step = p.steps.single()
            val departure = if (kind == "building_link") RouteMarkerKind.LinkDeparture else RouteMarkerKind.StairDeparture
            val arrival = if (kind == "building_link") RouteMarkerKind.LinkArrival else RouteMarkerKind.StairArrival
            assertEquals(listOf(RouteMarker(departure, g1, b, target), RouteMarker(arrival, target, c, g1)), step.markers)
            assertEquals(g1, step.from)
            assertEquals(target, step.to)
            assertTrue(p.segments.isEmpty())
        }
    }

    @Test fun same_plan_link_retains_authored_geometry_but_stairs_never_draw_lines() {
        val p = build(leg("building_link", g1, g1, listOf(a, c, b)))
        assertEquals(RoutePartKind.BuildingLink, p.segments.single().kind)
        assertEquals(listOf(a, c, b), p.segments.single().points)
        assertEquals(listOf(0), p.steps.single().segmentIndices)
        assertTrue(build(leg("stair_up", g1, g1)).segments.isEmpty())
    }

    @Test fun endpoints_use_graph_nodes_not_walk_starts_and_zero_is_valid() {
        val p = build(leg(points = listOf(b, c)), leg(floor = g2))
        assertEquals(listOf(RouteMarker(RouteMarkerKind.Start, g1, a),
            RouteMarker(RouteMarkerKind.Destination, u2, c)), p.endpoints)
        assertTrue(p.steps.all { it.markers.isEmpty() })
    }

    @Test fun invalid_points_reject_entire_leg_without_clamping_or_bridging() {
        for (value in listOf(Double.NaN, Double.POSITIVE_INFINITY, Double.NEGATIVE_INFINITY, -0.01, 1.01)) {
            for (bad in listOf(GraphPoint(value, 0.5), GraphPoint(0.5, value))) {
                for (kind in listOf("walk", "stair_up", "building_link")) {
                    val p = build(leg(kind, g1, g2.takeIf { kind != "walk" }, listOf(a, bad, b)))
                    assertEquals(setOf(RouteProblem.InvalidGeometry), p.steps.single().problems)
                    assertTrue(p.segments.all { it.points.isEmpty() })
                    assertTrue(p.steps.single().markers.isEmpty())
                    assertTrue(RouteProblem.InvalidGeometry in p.problems)
                }
            }
        }
    }

    @Test fun empty_geometry_keeps_known_step_and_missing_coordinate_problem() {
        for (kind in listOf("walk", "stair_down", "building_link")) {
            val p = build(leg(kind, g1, g2, emptyList()))
            assertTrue(RouteProblem.MissingCoordinates in p.steps.single().problems)
            assertTrue(p.segments.all { it.points.isEmpty() })
            assertTrue(p.steps.single().markers.isEmpty())
        }
    }

    @Test fun unknown_leg_does_not_become_walk_or_join_neighboring_steps() {
        val p = build(leg(), leg("teleport"), leg())
        assertEquals(listOf(0, 2), p.steps.map { it.id })
        assertEquals(listOf(0, 2), p.segments.map { it.legIndex })
        assertEquals(setOf(RouteProblem.UnsupportedLeg), p.problems)
    }

    @Test fun missing_raster_keeps_textual_step_and_local_problem() {
        val route = Route(10, listOf(leg(), leg("stair_up", g1, g2), leg(floor = g2)))
        val p = RoutePresentationBuilder.build(route, node("s"), node("d", g2), mapOf(g1 to RasterSize(1, 1)))
        assertTrue(p.steps[0].problems.isEmpty())
        assertEquals(setOf(RouteProblem.MissingMap), p.steps[1].problems)
        assertEquals(setOf(RouteProblem.MissingMap), p.steps[2].problems)
        assertEquals(setOf(RouteProblem.MissingMap), p.segments.last().problems)
        assertEquals(listOf(a, b), p.segments.last().points)
        assertEquals(setOf(RouteProblem.MissingMap), p.problems)
    }

    @Test fun empty_route_arrives_only_at_same_valid_endpoint() {
        val n = node("same")
        val p = RoutePresentationBuilder.build(Route(0, emptyList()), n, n, maps)
        assertTrue(p.arrived)
        assertTrue(p.steps.isEmpty())
        assertTrue(p.segments.isEmpty())
        assertEquals(listOf(a, a), p.endpoints.map { it.point })
        assertTrue(p.problems.isEmpty())
        for ((from, to) in listOf(n to node("other"), null to n, n to null, null to null,
            n to n.copy(x = Double.NaN))) {
            val missing = RoutePresentationBuilder.build(Route(0, emptyList()), from, to, maps)
            assertFalse(missing.arrived)
            assertTrue(RouteProblem.MissingCoordinates in missing.problems)
        }
    }

    @Test fun invalid_endpoint_is_omitted_without_replacing_it_with_leg_geometry() {
        val p = RoutePresentationBuilder.build(Route(1, listOf(leg())),
            node("s", point = GraphPoint(-1.0, 0.0)), null, maps)
        assertTrue(p.endpoints.isEmpty())
        assertTrue(RouteProblem.InvalidGeometry in p.problems)
        assertEquals(listOf(a, b), p.segments.single().points)
    }

    @Test fun raster_size_requires_positive_dimensions() {
        for ((w, h) in listOf(0 to 1, 1 to 0, -1 to 1, 1 to -1)) {
            assertThrows(IllegalArgumentException::class.java) { RasterSize(w, h) }
        }
        assertEquals(1, RasterSize(1, 1).width)
    }
}
