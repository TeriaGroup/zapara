package ru.bgtu_voenmeh.zapara.ui.maps

import java.io.File
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.campus.Route

class RouteNavigationTest {
    private val a = FloorKey("ГК", 1)
    private val b = FloorKey("ГК", 2)
    private val c = FloorKey("УЛК", 1)
    private fun step(id: Int, from: FloorKey, to: FloorKey = from, kind: RoutePartKind = RoutePartKind.Walk) =
        RouteStep(id, kind, from, to, emptyList(), emptyList(), emptySet())
    private fun state(steps: List<RouteStep> = listOf(step(0, a), step(3, a, b, RoutePartKind.StairUp), step(4, b), step(7, a))) =
        MapsUiState(route = Route(60, emptyList()), presentation = RoutePresentation(steps, emptyList(), emptyList(), false, emptySet()),
            activeStepId = steps.firstOrNull()?.id, fullscreen = true, zoom = 2f, fitGeneration = 8)

    @Test fun select_preserves_identity_and_closes_other_surfaces() {
        val old = state().copy(showStack = true, stepsOpen = true)
        val next = RouteNavigation.select(old, 4)
        assertEquals("ГК", next.building)
        assertEquals(2, next.floor)
        assertEquals(4, next.activeStepId)
        assertEquals(9, next.fitGeneration)
        assertEquals(1f, next.zoom)
        assertFalse(next.showStack)
        assertFalse(next.stepsOpen)
        assertTrue(next.fullscreen)
        assertSame(old.route, next.route)
        assertSame(old.presentation, next.presentation)
    }

    @Test fun repeated_id_keeps_fit_but_return_from_manual_floor_fits() {
        val old = state()
        assertEquals(8, RouteNavigation.select(old, 0).fitGeneration)
        assertEquals(2f, RouteNavigation.select(old, 0).zoom)
        val manual = old.copy(building = "УЛК", floor = 5)
        val back = RouteNavigation.select(manual, 0)
        assertEquals(a, FloorKey(back.building, back.floor))
        assertEquals(9, back.fitGeneration)
        assertSame(old.route, back.route)
    }

    @Test fun move_uses_order_not_leg_id_arithmetic_or_manual_floor() {
        val old = state().copy(building = "УЛК", floor = 5)
        val next = RouteNavigation.move(old, 1)
        assertEquals(3, next.activeStepId)
        assertEquals(a, FloorKey(next.building, next.floor))
        assertEquals(0, RouteNavigation.move(next, -1).activeStepId)
        assertEquals(4, RouteNavigation.move(next, 1).activeStepId)
        assertSame(old, RouteNavigation.move(old, -1))
        val last = RouteNavigation.select(old, 7)
        assertSame(last, RouteNavigation.move(last, 1))
    }

    @Test fun unknown_empty_null_selection_are_noops() {
        val old = state()
        assertSame(old, RouteNavigation.select(old, 99))
        assertSame(old, RouteNavigation.move(old, 2))
        val unselected = old.copy(activeStepId = null)
        assertSame(unselected, RouteNavigation.move(unselected, 1))
        val empty = state(emptyList())
        assertSame(empty, RouteNavigation.move(empty, 1))
        assertSame(empty, RouteNavigation.select(empty, 0))
    }

    @Test fun terminal_stair_uses_arrival_floor() {
        val old = state(listOf(step(0, a), step(2, a, b, RoutePartKind.StairUp)))
        val next = RouteNavigation.select(old, 2)
        assertEquals(b, FloorKey(next.building, next.floor))
        assertSame(next, RouteNavigation.move(next, 1))
    }

    @Test fun terminal_link_uses_target_building_and_its_raster_only() {
        val main = FloorRaster(File("main.jpg"), RasterSize(10, 10))
        val ulk = FloorRaster(File("ulk.jpg"), RasterSize(20, 10))
        val old = state(listOf(step(0, a), step(3, a, c, RoutePartKind.BuildingLink)))
            .copy(rasterCatalog = mapOf(a to main, c to ulk))
        val next = RouteNavigation.select(old, 3)
        assertEquals(c, FloorKey(next.building, next.floor))
        assertEquals(ulk.file, next.planFile)
        assertEquals(mapOf(1 to ulk.file), next.floorFiles)
        assertTrue(next.path.isEmpty())
        assertNull(next.highlight)
    }

    @Test fun nonterminal_link_uses_departure_then_next_arrival() {
        val old = state(listOf(step(0, a, c, RoutePartKind.BuildingLink), step(2, c)))
        assertEquals(a, FloorKey(RouteNavigation.select(old, 0).building, RouteNavigation.select(old, 0).floor))
        assertEquals("УЛК", RouteNavigation.move(old, 1).building)
    }

    @Test fun revisiting_same_floor_is_a_distinct_step_and_fit_context() {
        val old = state()
        val next = RouteNavigation.select(old, 7)
        assertEquals(7, next.activeStepId)
        assertEquals(9, next.fitGeneration)
        assertSame(old.route, next.route)
    }
}
