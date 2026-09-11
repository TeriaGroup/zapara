package ru.bgtu_voenmeh.zapara.ui.maps

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.campus.Leg
import ru.bgtu_voenmeh.zapara.data.campus.GraphPoint
import ru.bgtu_voenmeh.zapara.data.campus.Route
import ru.bgtu_voenmeh.zapara.data.campus.CampusGraph
import ru.bgtu_voenmeh.zapara.data.campus.CampusRouter

class CampusMapProjectionTest {
    @Test fun singleton_stair_keeps_source_and_arrival_markers_after_loading_and_routing() {
        val graph = CampusGraph.load("""
            {"version":1,"buildings":["УЛК"],"nodes":[
              {"id":"lower","kind":"stair","building":"УЛК","floor":1,"x":0.2,"y":0.3,"group":"shaft"},
              {"id":"upper","kind":"stair","building":"УЛК","floor":2,"x":0.2,"y":0.3,"group":"shaft"}
            ],"edges":[{"from":"lower","to":"upper","kind":"stair_up","seconds":20,"oneWay":false,"points":[[0.2,0.3]]}]}
        """.trimIndent())
        val route = CampusRouter.find(graph, "lower", "upper").route!!

        assertEquals(1, route.legs.single().points.size)
        assertEquals(listOf(StairMarkerUi(.2, .3, "↑ 2", true)), MapsComposer.stairMarkers(route, "УЛК", 1))
        assertEquals(listOf(StairMarkerUi(.2, .3, "↑ с 1", false)), MapsComposer.stairMarkers(route, "УЛК", 2))
    }

    @Test fun stair_only_intermediate_floor_prioritizes_onward_departure() {
        for ((kind, floors, expected) in listOf(
            Triple("stair_up", listOf(1, 2, 3), "↑ 3"),
            Triple("stair_down", listOf(3, 2, 1), "↓ 1")
        )) {
            val route = Route(40, listOf(
                Leg(kind, "УЛК", floors[0], "УЛК", floors[1], listOf(GraphPoint(.2, .3), GraphPoint(.3, .4))),
                Leg(kind, "УЛК", floors[1], "УЛК", floors[2], listOf(GraphPoint(.3, .4), GraphPoint(.4, .5)))
            ))
            assertTrue(MapsComposer.floorPathStrokes(route, "УЛК", 2).isEmpty())
            assertEquals(listOf(StairMarkerUi(.3, .4, expected, true)), MapsComposer.stairMarkers(route, "УЛК", 2))
            assertEquals(2, route.legs.size)
        }
    }

    @Test fun markers_use_source_and_target_coordinates_and_filter_buildings() {
        for ((kind, from, to) in listOf(Triple("stair_up", 1, 2), Triple("stair_down", 2, 1))) {
            val arrow = if (kind == "stair_up") "↑" else "↓"
            val route = Route(40, listOf(
                Leg(kind, "ГК", from, "ГК", to, listOf(GraphPoint(.2, .3), GraphPoint(.4, .5))),
                Leg(kind, "УЛК", from, "УЛК", to, listOf(GraphPoint(.6, .7), GraphPoint(.8, .9)))
            ))
            assertEquals(listOf(StairMarkerUi(.2, .3, "$arrow $to", true)), MapsComposer.stairMarkers(route, "ГК", from))
            assertEquals(listOf(StairMarkerUi(.4, .5, "$arrow с $from", false)), MapsComposer.stairMarkers(route, "ГК", to))
            assertEquals(listOf(StairMarkerUi(.8, .9, "$arrow с $from", false)), MapsComposer.stairMarkers(route, "УЛК", to))
            assertTrue(MapsComposer.stairMarkers(route, "ГК", 5).isEmpty())
        }
    }

    @Test fun revisited_floor_keeps_two_stair_markers_and_separate_walks() {
        val route = Route(60, listOf(
            Leg("walk", "ГК", 3, null, null, listOf(GraphPoint(.1, .2), GraphPoint(.2, .3))),
            Leg("stair_down", "ГК", 3, "ГК", 2, listOf(GraphPoint(.2, .3), GraphPoint(.25, .35))),
            Leg("walk", "ГК", 2, null, null, listOf(GraphPoint(.25, .35), GraphPoint(.75, .65))),
            Leg("stair_up", "ГК", 2, "ГК", 3, listOf(GraphPoint(.75, .65), GraphPoint(.8, .7))),
            Leg("walk", "ГК", 3, null, null, listOf(GraphPoint(.8, .7), GraphPoint(.9, .8)))
        ))
        assertEquals(listOf(StairMarkerUi(.2, .3, "↓ 2", true), StairMarkerUi(.8, .7, "↑ с 2", false)),
            MapsComposer.stairMarkers(route, "ГК", 3))
        assertEquals(listOf(listOf(.1f to .2f, .2f to .3f), listOf(.8f to .7f, .9f to .8f)),
            MapsComposer.floorPathStrokes(route, "ГК", 3))
    }

    @Test fun yard_transfer_never_connects_coordinates_of_different_plans() {
        for ((from, to) in listOf("ГК" to "УЛК", "УЛК" to "ГК")) {
            val route = Route(30, listOf(
                Leg("walk", from, 1, null, null, listOf(GraphPoint(.2, .8), GraphPoint(.3, .8))),
                Leg("building_link", from, 1, to, 1, listOf(GraphPoint(.3, .8), GraphPoint(.7, .2))),
                Leg("walk", to, 1, null, null, listOf(GraphPoint(.7, .2), GraphPoint(.8, .2)))
            ))
            assertEquals(listOf(listOf(.2f to .8f, .3f to .8f)), MapsComposer.floorPathStrokes(route, from, 1))
            assertEquals(listOf(listOf(.7f to .2f, .8f to .2f)), MapsComposer.floorPathStrokes(route, to, 1))
            assertEquals(1, CampusStackProjector.routePolylines(route, from).size)
            assertEquals(1, CampusStackProjector.routePolylines(route, to).size)
        }
    }
}
