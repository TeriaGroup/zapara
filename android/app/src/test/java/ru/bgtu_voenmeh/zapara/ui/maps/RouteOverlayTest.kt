package ru.bgtu_voenmeh.zapara.ui.maps

import java.io.File
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.campus.GraphPoint

class RouteOverlayTest {
    private val floor = FloorKey("ГК", 1)
    private val fit = FittedImage(0f, 75f, 300f, 150f)
    private val segment = RouteSegment(0, floor, RoutePartKind.Walk,
        listOf(GraphPoint(0.0, 0.0), GraphPoint(1.0, 1.0)))

    @Test fun fit_applied_once_including_letterbox() {
        assertEquals(listOf(PathPx(0f, 75f), PathPx(300f, 225f)), routeOverlayPoints(segment, floor, fit))
        assertTrue(routeOverlayPoints(segment, FloorKey("УЛК", 1), fit).isEmpty())
    }
    @Test fun invalid_geometry_and_transitions_never_create_a_bridge() {
        assertTrue(routeOverlayPoints(segment.copy(kind = RoutePartKind.StairUp), floor, fit).isEmpty())
        assertTrue(routeOverlayPoints(segment.copy(points = listOf(GraphPoint(Double.NaN, 0.0))), floor, fit).isEmpty())
        assertTrue(routeOverlayPoints(segment.copy(problems = setOf(RouteProblem.InvalidGeometry)), floor, fit).isEmpty())
        assertEquals(2, routeOverlayPoints(segment.copy(problems = setOf(RouteProblem.MissingMap)), floor, fit).size)
    }

    @Test fun on_map_numbers_cancel_font_scale_and_use_compact_padding() {
        val source = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/maps/RouteMapOverlay.kt").readText()
        assertTrue(source.contains("fun compactMapNumberStyle()"))
        assertTrue(source.contains("(11f / scale).sp"))
        assertTrue(source.contains("padding(RouteOverlayStyle.numberPadding)"))
        assertTrue(source.contains("val numberPadding = 2.dp"))
    }
}
