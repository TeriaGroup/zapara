package ru.bgtu_voenmeh.zapara.ui.maps

import java.io.File
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.CoordsRect
import ru.bgtu_voenmeh.zapara.data.campus.Route
import ru.bgtu_voenmeh.zapara.data.campus.CampusGraph
import ru.bgtu_voenmeh.zapara.data.campus.Node
import ru.bgtu_voenmeh.zapara.ui.XmlCopy

class MapRouteStateTest {
    @Test fun success_clears_prior_failure_and_display_projection_retains_route_identity() {
        val node = Node("a", "room", "ГК", 1, .1, .2, room = "101")
        val graph = CampusGraph(1, listOf("ГК"), listOf(node), emptyList())
        val route = Route(0, emptyList())
        val presentation = RoutePresentationBuilder.build(route, node, node, emptyMap())
        val result = MapRouteResult(node, node, node, route, presentation, emptyMap(), null)
        val old = MapsUiState(routeFailure = "old", fullscreen = true, activeStepId = null)
        val next = MapRouteState.decorate(old, "ГК", graph, "a", "a", null, result, null, XmlCopy)
        assertNull(next.routeFailure)
        assertFalse(next.routeLoading)
        assertSame(route, next.route)
        assertSame(presentation, next.presentation)
        assertTrue(next.presentation!!.arrived)
        assertTrue(next.fullscreen)
        assertFalse(next.canSwap)
    }

    @Test fun distinct_failures_have_distinct_russian_resource_copy() {
        val keys = listOf("maps_route_graph_missing", "maps_route_unknown", "maps_route_unreachable", "maps_route_load_failed")
        val messages = keys.map { XmlCopy.get(it) }
        assertEquals(4, messages.distinct().size)
        assertTrue(messages.all { it.any { c -> c in 'А'..'я' } && !it.contains("maps_route") })
    }

    @Test fun changed_endpoints_hide_all_old_geometry_atomically_but_keep_last_good_maps() {
        val file = File("floor.jpg")
        val catalog = mapOf(FloorKey("ГК", 1) to FloorRaster(file, RasterSize(10, 10)))
        val old = MapsUiState(route = Route(5, emptyList()),
            presentation = RoutePresentation(emptyList(), emptyList(), emptyList(), true, emptySet()),
            activeStepId = 3, routeFailure = "old failure", path = listOf(listOf(.1f to .2f)),
            stairMarkers = listOf(StairMarkerUi(.1, .2, "up", true)),
            highlight = HighlightUi(CoordsRect(.1, .1, .1, .1), "old room"),
            planFile = file, rasterCatalog = catalog, fullscreen = true, fromLabel = "old", toLabel = "old")
        val next = MapRouteState.begin(old, "A", "B")
        assertTrue(next.routeLoading)
        assertNull(next.route)
        assertNull(next.presentation)
        assertNull(next.activeStepId)
        assertNull(next.routeFailure)
        assertNull(next.highlight)
        assertTrue(next.path.isEmpty())
        assertTrue(next.stairMarkers.isEmpty())
        assertTrue(next.routeSteps.isEmpty())
        assertSame(catalog, next.rasterCatalog)
        assertSame(file, next.planFile)
        assertEquals("A", next.fromLabel)
        assertEquals("B", next.toLabel)
        assertTrue(next.fullscreen)
    }
}
