package ru.bgtu_voenmeh.zapara.ui.maps

import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.campus.Leg
import ru.bgtu_voenmeh.zapara.data.campus.Route

class RouteProblemTextTest {
    @Test fun empty_and_unknown_leg_have_visible_unrepresented_messages() {
        val empty = RoutePresentationBuilder.build(Route(0, emptyList()), null, null, emptyMap())
        assertFalse(empty.arrived)
        assertTrue(R.string.maps_missing_coordinates in unrepresentedRouteProblems(empty))
        val unknown = RoutePresentationBuilder.build(Route(1, listOf(Leg("unknown", "ГК", 1, null, null, emptyList()))),
            null, null, emptyMap())
        assertTrue(R.string.maps_invalid_geometry in unrepresentedRouteProblems(unknown))
    }

    @Test fun step_messages_are_deduplicated_by_visible_meaning_without_mutating_geometry() {
        val floor = FloorKey("ГК", 1)
        val step = RouteStep(4, RoutePartKind.Walk, floor, floor, emptyList(), emptyList(), setOf(RouteProblem.InvalidGeometry))
        val presentation = RoutePresentation(listOf(step), emptyList(), emptyList(), false,
            setOf(RouteProblem.InvalidGeometry, RouteProblem.UnsupportedLeg, RouteProblem.MissingCoordinates, RouteProblem.MissingMap))
        val messages = unrepresentedRouteProblems(presentation)
        assertEquals(setOf(R.string.maps_missing_coordinates, R.string.maps_map_unavailable), messages.toSet())
        assertSame(step, presentation.steps.single())
    }
}
