package ru.bgtu_voenmeh.zapara.data.campus

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.ui.XmlCopy
import ru.bgtu_voenmeh.zapara.ui.maps.MapsComposer
import java.io.File
import java.time.LocalDate
import java.time.LocalDateTime

class CampusRouterTest {
    private fun bundled() = CampusGraph.load(File("src/main/assets/maps/campus-graph.json").readText())

    @Test fun bundled_ulk_entrance_reaches_564() {
        val graph = bundled()
        assertTrue(CampusRouter.find(graph, "ulk.entrance.main", "ulk.room.564").ok)
        assertFalse(CampusRouter.find(graph, "ulk.entrance.hostel", "ulk.room.564").ok)
        val start = MapsComposer.startFor(graph, "ulk.entrance.hostel", "ulk.room.564")
        assertEquals("ulk.entrance.main", start?.id)
        assertTrue(CampusRouter.find(graph, start!!.id, "ulk.room.564").ok)
        assertNull(MapsComposer.startFor(graph, null, "ulk.room.564"))
    }

    @Test fun ulk_main_to_564_climbs_east_stairs_not_lobby_mid() {
        val graph = bundled()
        val to564 = CampusRouter.find(graph, "ulk.entrance.main", "ulk.room.564")
        val stairs = to564.route!!.legs.filter { it.kind == "stair_up" || it.kind == "stair_down" }
            .map { leg ->
                graph.nodes.first { n ->
                    n.kind == "stair" && n.building == leg.building && n.floor == leg.floor &&
                        n.x == leg.points.first().x && n.y == leg.points.first().y
                }.group
            }
            .distinct()
        assertEquals(listOf("ulk.stair.east"), stairs)
        assertTrue(CampusRouter.find(graph, "ulk.entrance.main", "ulk.stair.mid.1").ok)
        assertTrue(CampusRouter.find(graph, "ulk.entrance.main", "ulk.stair.east.1").ok)
    }

    @Test fun exact_node_id_wins_over_a_room_label() {
        val graph = CampusGraph(1, listOf("ГК"), listOf(
            Node("room", "room", "ГК", 1, .1, .1, room = "target"),
            Node("target", "junction", "ГК", 1, .2, .1)
        ), emptyList())

        assertEquals("target", CampusRouter.resolve(graph, "target")?.id)
    }

    @Test fun ambiguous_generic_room_label_is_unresolved() {
        val graph = CampusGraph(1, listOf("ГК", "УЛК"), listOf(
            Node("a", "room", "ГК", 1, .1, .1, room = "101"),
            Node("b", "room", "УЛК", 1, .2, .1, room = "101")
        ), emptyList())

        assertNull(CampusRouter.resolve(graph, "101"))
        assertEquals("unknown_place", CampusRouter.findResolved(graph, "101", "a").failure)
        assertEquals("b", CampusRouter.resolve(graph, "b")?.id)
    }

    @Test fun walk_chord_through_a_doorless_box_is_skipped_when_a_corridor_exists() {
        val graph = CampusGraph(1, listOf("ГК"), listOf(
            Node("start", "junction", "ГК", 1, .1, .5),
            Node("goal", "junction", "ГК", 1, .9, .5),
            Node("corridor", "junction", "ГК", 1, .5, .1)
        ), listOf(
            Edge("start", "goal", "walk", 1.0, false, listOf(GraphPoint(.1, .5), GraphPoint(.5, .5), GraphPoint(.9, .5))),
            Edge("start", "corridor", "walk", 5.0, false),
            Edge("corridor", "goal", "walk", 5.0, false)
        ), listOf(BlockedRegion("ГК", 1, .4, .4, .6, .6)))

        val route = CampusRouter.find(graph, "start", "goal").route!!
        assertEquals(10, route.seconds)
        assertTrue(route.legs.none { leg ->
            leg.points.any { p -> p.x > .4 && p.x < .6 && p.y > .4 && p.y < .6 }
        })
    }

    @Test fun approach_into_a_room_may_enter_that_room_keep_out() {
        val graph = CampusGraph(1, listOf("ГК"), listOf(
            Node("door", "junction", "ГК", 1, .1, .5),
            Node("classroom", "room", "ГК", 1, .5, .5, room = "101")
        ), listOf(
            Edge("door", "classroom", "walk", 1.0, false, listOf(GraphPoint(.1, .5), GraphPoint(.5, .5)))
        ), listOf(BlockedRegion("ГК", 1, .4, .4, .6, .6, ownerId = "classroom")))

        assertTrue(CampusRouter.find(graph, "door", "classroom").ok)
    }

    @Test fun unlabeled_keep_out_blocks_an_edge_that_ends_inside_it() {
        val graph = CampusGraph(1, listOf("ГК"), listOf(
            Node("door", "junction", "ГК", 1, .1, .5),
            Node("inside", "junction", "ГК", 1, .5, .5)
        ), listOf(Edge("door", "inside", "walk", 1.0, false)),
            listOf(BlockedRegion("ГК", 1, .4, .4, .6, .6)))

        assertEquals("unreachable", CampusRouter.find(graph, "door", "inside").failure)
    }

    @Test fun room_nodes_are_endpoints_and_never_corridor_shortcuts() {
        val graph = roomShortcutGraph()
        assertEquals(10, CampusRouter.find(graph, "start", "goal").route!!.seconds)
        assertEquals(1, CampusRouter.find(graph, "classroom", "goal").route!!.seconds)
        assertEquals(1, CampusRouter.find(graph, "start", "classroom").route!!.seconds)
    }

    @Test fun route_is_unreachable_when_it_would_require_crossing_an_intermediate_room() {
        val original = roomShortcutGraph()
        val graph = original.copy(edges = original.edges.take(2))
        assertEquals("unreachable", CampusRouter.find(graph, "start", "goal").failure)
    }

    private fun roomShortcutGraph() = CampusGraph(1, listOf("ГК"), listOf(
        Node("start", "junction", "ГК", 1, .1, .1), Node("goal", "junction", "ГК", 1, .9, .1),
        Node("classroom", "room", "ГК", 1, .5, .1, room = "101"), Node("corridor", "junction", "ГК", 1, .5, .5)
    ), listOf(Edge("start", "classroom", "walk", 1.0, false), Edge("classroom", "goal", "walk", 1.0, false),
        Edge("start", "corridor", "walk", 5.0, false), Edge("corridor", "goal", "walk", 5.0, false)))

    @Test fun floor_changing_links_do_not_hide_a_faster_route() {
        val graph = CampusGraph(1, listOf("ГК", "УЛК"), listOf(
            Node("start", "building_link", "ГК", 2, .1, .1),
            Node("goal", "building_link", "ГК", 2, .9, .1),
            Node("bridge", "building_link", "УЛК", 1, .5, .5),
            Node("stair1", "stair", "ГК", 1, .1, .1, group = "shaft"),
            Node("stair2", "stair", "ГК", 2, .1, .1, group = "shaft")
        ), listOf(Edge("start", "goal", "walk", 50.0, false),
            Edge("start", "bridge", "building_link", 1.0, false), Edge("bridge", "goal", "building_link", 1.0, false),
            Edge("stair1", "stair2", "stair_up", 100.0, false)))

        val route = CampusRouter.find(graph, "start", "goal").route!!
        assertEquals(2, route.seconds)
        assertEquals(listOf("building_link", "building_link"), route.legs.map { it.kind })
    }

    @Test fun equal_time_routes_choose_fewer_stairs_in_both_directions() {
        for ((from, to) in listOf("start" to "goal", "goal" to "start")) {
            val route = CampusRouter.find(stairChoiceGraph(), from, to).route!!
            assertEquals(3, route.seconds)
            assertTrue(route.legs.all { it.kind == "walk" })
        }
    }

    @Test fun faster_stair_route_wins_over_a_slower_corridor_route() {
        val original = stairChoiceGraph()
        val graph = original.copy(edges = original.edges.map {
            if (it.from == "corridor" || it.to == "corridor") it.copy(seconds = 10.0) else it
        })
        val route = CampusRouter.find(graph, "start", "goal").route!!
        assertEquals(3, route.seconds)
        assertEquals(listOf("stair_up", "walk", "stair_down"), route.legs.map { it.kind })
    }

    @Test fun equal_time_routes_are_stable_when_input_order_changes() {
        val graph = CampusGraph(1, listOf("ГК"), listOf(
            Node("start", "junction", "ГК", 1, .1, .5), Node("a", "junction", "ГК", 1, .2, .1),
            Node("b", "junction", "ГК", 1, .2, .9), Node("goal", "junction", "ГК", 1, .9, .5)
        ), listOf(Edge("start", "b", "walk", 1.0, false), Edge("b", "goal", "walk", 1.0, false),
            Edge("start", "a", "walk", 1.0, false), Edge("a", "goal", "walk", 1.0, false)))

        for (ordered in listOf(graph, graph.copy(nodes = graph.nodes.reversed(), edges = graph.edges.reversed()))) {
            val route = CampusRouter.find(ordered, "start", "goal").route!!
            assertEquals(GraphPoint(.2, .1), route.legs.first().points.last())
        }
    }

    @Test fun reversed_stair_respects_one_way_and_reverses_direction_and_geometry() {
        val down = Edge("upper", "lower", "stair_down", 20.0, true,
            listOf(GraphPoint(.2, .2), GraphPoint(.3, .4), GraphPoint(.7, .8)))
        val graph = CampusGraph(1, listOf("ГК"), listOf(
            Node("upper", "stair", "ГК", 2, .2, .2, group = "s"),
            Node("lower", "stair", "ГК", 1, .7, .8, group = "s")
        ), listOf(down))

        assertEquals("unreachable", CampusRouter.find(graph, "lower", "upper").failure)
        val leg = CampusRouter.find(graph.copy(edges = listOf(down.copy(oneWay = false))), "lower", "upper").route!!.legs.single()
        assertEquals("stair_up", leg.kind)
        assertEquals(1, leg.floor)
        assertEquals(2, leg.toFloor)
        assertEquals(listOf(GraphPoint(.7, .8), GraphPoint(.3, .4), GraphPoint(.2, .2)), leg.points)
    }

    private fun stairChoiceGraph() = CampusGraph(1, listOf("ГК"), listOf(
        Node("start", "stair", "ГК", 1, .1, .1, group = "a"),
        Node("up", "stair", "ГК", 2, .1, .1, group = "a"),
        Node("across", "stair", "ГК", 2, .9, .1, group = "b"),
        Node("goal", "stair", "ГК", 1, .9, .1, group = "b"),
        Node("corridor", "junction", "ГК", 1, .5, .1),
        Node("cheap1", "stair", "ГК", 1, .5, .5, group = "cheap"),
        Node("cheap2", "stair", "ГК", 2, .5, .5, group = "cheap")
    ), listOf(Edge("start", "up", "stair_up", 1.0, false), Edge("up", "across", "walk", 1.0, false),
        Edge("across", "goal", "stair_down", 1.0, false), Edge("start", "corridor", "walk", 2.5, false),
        Edge("corridor", "goal", "walk", .5, false), Edge("cheap1", "cheap2", "stair_up", .1, false)))

    @Test fun fractional_route_duration_rounds_half_seconds_up_on_both_platforms() {
        val graph = CampusGraph(1, listOf("УЛК"), listOf(
            Node("a", "room", "УЛК", 1, 0.1, 0.1),
            Node("b", "room", "УЛК", 1, 0.2, 0.1)
        ), listOf(Edge("a", "b", "walk", 2.5, false)))

        assertEquals(3, CampusRouter.find(graph, "a", "b").route!!.seconds)
    }

    @Test fun previous_lesson_today_ignores_when_next_is_tomorrow() {
        val today = Lesson(timeStart = "09:00", timeEnd = "10:35", classroomRaw = "A1")
        val tomorrow = Lesson(timeStart = "10:45", timeEnd = "12:20", classroomRaw = "A1")
        val mondayEve = LocalDateTime.of(2026, 9, 7, 18, 0)
        assertNull(
            MapsComposer.previousLessonToday(listOf(today), tomorrow, mondayEve, LocalDate.of(2026, 9, 8))
        )
        val afternoon = Lesson(timeStart = "12:40", timeEnd = "14:15", classroomRaw = "B1")
        assertEquals(
            today,
            MapsComposer.previousLessonToday(
                listOf(today, afternoon), afternoon, LocalDateTime.of(2026, 9, 7, 12, 0), LocalDate.of(2026, 9, 7)
            )
        )
    }

    @Test fun route_picker_list_shrinks_with_ime_instead_of_fixed_420dp() {
        val sheet = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/maps/RoutePickerSheet.kt").readText()
        val chrome = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/components/Controls.kt").readText()
        assertTrue(chrome.contains("BoxWithConstraints"))
        assertTrue(sheet.contains("weight(1f)"))
        assertFalse(sheet.contains("max = 420.dp"))
    }

    @Test fun campusGraph_always_reads_assets_not_filesDir() {
        val src = File("src/main/java/ru/bgtu_voenmeh/zapara/data/MapStore.kt").readText()
        assertTrue(src.contains("assets.open(\"maps/campus-graph.json\")"))
        assertFalse(src.contains("File(dir, \"campus-graph.json\")"))
        assertTrue(src.contains("last-entrance.txt"))
    }

    @Test fun bundled_graph_loads_and_routes_320() {
        val json = File("src/main/assets/maps/campus-graph.json").readText()
        val graph = CampusGraph.load(json)
        assertEquals(1, graph.version)
        assertEquals(listOf("ГК", "УЛК"), graph.buildings)
        assertTrue(graph.nodes.isNotEmpty())
        assertTrue(graph.blocked.isNotEmpty())
        assertTrue(graph.blocked.any { it.ownerId == null })
        assertTrue(CampusRouter.entrances(graph).isNotEmpty())
        val result = CampusRouter.findFromEntrance(graph, "ulk.entrance.main", "320")
        assertTrue(result.ok)
        assertNotNull(result.route)
        assertEquals("маршрут ещё не размечен", XmlCopy.get("route_unmarked"))
    }

    @Test fun findFromEntrance_missing_entrance_is_unknown_place() {
        val result = CampusRouter.findFromEntrance(entranceGraph(), "no.such", "A1")
        assertFalse(result.ok)
        assertNull(result.route)
        assertEquals("unknown_place", result.failure)
    }

    @Test fun findFromEntrance_missing_room_is_unknown_place() {
        val result = CampusRouter.findFromEntrance(entranceGraph(), "lab.entrance", "no.room")
        assertFalse(result.ok)
        assertEquals("unknown_place", result.failure)
    }

    @Test fun findFromEntrance_room_id_as_entrance_is_unknown_place() {
        val result = CampusRouter.findFromEntrance(entranceGraph(), "lab.room.a", "A1")
        assertFalse(result.ok)
        assertEquals("unknown_place", result.failure)
    }

    @Test fun findFromEntrance_unreachable_is_unreachable() {
        val result = CampusRouter.findFromEntrance(entranceGraph(), "lab.entrance", "B1")
        assertFalse(result.ok)
        assertEquals("unreachable", result.failure)
    }

    @Test fun findFromEntrance_resolves_room_key_and_strips_junk() {
        val result = CampusRouter.findFromEntrance(entranceGraph(), "lab.entrance", "A1*;")
        assertTrue(result.ok)
        assertNotNull(result.route)
        assertTrue(result.route!!.legs.any { it.kind == "walk" })
    }

    @Test fun findFromEntrance_labyrinth_climbs_from_floor1() {
        val route = CampusRouter.findFromEntrance(labyrinthWithEntrance(), "lab.entrance", "E3").route
        assertNotNull(route)
        assertTrue(route!!.legs.any { it.kind == "stair_up" })
        assertTrue(route.legs.any { it.kind == "walk" && it.floor == 1 })
        assertFalse(route.legs.any { it.kind == "walk" && it.floor == 3 && it.points.size > 2 })
    }

    @Test fun find_between_room_keys_descends_then_climbs() {
        val result = CampusRouter.findResolved(labyrinthWithEntrance(), "W3", "E3")
        assertTrue(result.ok)
        assertTrue(result.route!!.legs.any { it.kind == "stair_down" })
        assertTrue(result.route.legs.any { it.kind == "stair_up" })
    }

    @Test fun chooseFrom_prefers_previous_room_else_entrance() {
        val g = entranceGraph()
        assertEquals("lab.room.a", CampusRouter.chooseFrom(g, "A1", "lab.entrance")!!.id)
        assertEquals("lab.entrance", CampusRouter.chooseFrom(g, "no.room", "lab.entrance")!!.id)
        assertNull(CampusRouter.chooseFrom(g, "no.room", "no.entrance"))
    }

    @Test fun floorPathStrokes_keeps_disconnected_floor3_wings() {
        val route = CampusRouter.findResolved(labyrinthWithEntrance(), "W3", "E3").route!!
        val f3 = MapsComposer.floorPathStrokes(route, "УЛК", 3)
        assertEquals(2, f3.size)
        assertEquals(listOf(0.2f to 0.2f, 0.2f to 0.5f), f3[0])
        assertEquals(listOf(0.8f to 0.5f, 0.8f to 0.2f), f3[1])
        val f1 = MapsComposer.floorPathStrokes(route, "УЛК", 1)
        assertEquals(1, f1.size)
        assertEquals(listOf(0.2f to 0.5f, 0.5f to 0.5f, 0.8f to 0.5f), f1[0])
        assertTrue(MapsComposer.floorPathStrokes(route, "УЛК", 2).isEmpty())
    }

    private fun entranceGraph() = CampusGraph.load(
        """
        {"version":1,"buildings":["УЛК"],"nodes":[
          {"id":"lab.entrance","kind":"entrance","building":"УЛК","floor":1,"x":0.1,"y":0.9,"label":"Вход лабиринта"},
          {"id":"lab.room.a","kind":"room","building":"УЛК","floor":1,"x":0.2,"y":0.2,"room":"A1"},
          {"id":"lab.room.b","kind":"room","building":"УЛК","floor":1,"x":0.8,"y":0.2,"room":"B1"}
        ],"edges":[{"from":"lab.entrance","to":"lab.room.a","kind":"walk","seconds":10,"oneWay":false}]}
        """.trimIndent()
    )

    private fun labyrinthWithEntrance() = CampusGraph.load(
        """
        {
          "version": 1,
          "buildings": ["УЛК"],
          "nodes": [
            {"id":"lab.entrance","kind":"entrance","building":"УЛК","floor":1,"x":0.1,"y":0.9,"label":"Вход лабиринта"},
            {"id":"lab.room.west.3","kind":"room","building":"УЛК","floor":3,"x":0.2,"y":0.2,"room":"W3"},
            {"id":"lab.stair.west.3","kind":"stair","building":"УЛК","floor":3,"x":0.2,"y":0.5,"group":"lab.stair.west"},
            {"id":"lab.stair.west.2","kind":"stair","building":"УЛК","floor":2,"x":0.2,"y":0.5,"group":"lab.stair.west"},
            {"id":"lab.stair.west.1","kind":"stair","building":"УЛК","floor":1,"x":0.2,"y":0.5,"group":"lab.stair.west"},
            {"id":"lab.room.east.3","kind":"room","building":"УЛК","floor":3,"x":0.8,"y":0.2,"room":"E3"},
            {"id":"lab.stair.east.3","kind":"stair","building":"УЛК","floor":3,"x":0.8,"y":0.5,"group":"lab.stair.east"},
            {"id":"lab.stair.east.2","kind":"stair","building":"УЛК","floor":2,"x":0.8,"y":0.5,"group":"lab.stair.east"},
            {"id":"lab.stair.east.1","kind":"stair","building":"УЛК","floor":1,"x":0.8,"y":0.5,"group":"lab.stair.east"}
          ],
          "edges": [
            {"from":"lab.entrance","to":"lab.stair.west.1","kind":"walk","seconds":5,"oneWay":false},
            {"from":"lab.room.west.3","to":"lab.stair.west.3","kind":"walk","seconds":10,"oneWay":false},
            {"from":"lab.room.east.3","to":"lab.stair.east.3","kind":"walk","seconds":10,"oneWay":false},
            {"from":"lab.stair.west.3","to":"lab.stair.west.2","kind":"stair_down","seconds":20,"oneWay":true},
            {"from":"lab.stair.west.2","to":"lab.stair.west.1","kind":"stair_down","seconds":20,"oneWay":true},
            {"from":"lab.stair.west.1","to":"lab.stair.west.2","kind":"stair_up","seconds":20,"oneWay":true},
            {"from":"lab.stair.west.2","to":"lab.stair.west.3","kind":"stair_up","seconds":20,"oneWay":true},
            {"from":"lab.stair.east.3","to":"lab.stair.east.2","kind":"stair_down","seconds":20,"oneWay":true},
            {"from":"lab.stair.east.2","to":"lab.stair.east.1","kind":"stair_down","seconds":20,"oneWay":true},
            {"from":"lab.stair.east.1","to":"lab.stair.east.2","kind":"stair_up","seconds":20,"oneWay":true},
            {"from":"lab.stair.east.2","to":"lab.stair.east.3","kind":"stair_up","seconds":20,"oneWay":true},
            {"from":"lab.stair.west.1","to":"lab.stair.east.1","kind":"walk","seconds":30,"oneWay":false,"points":[[0.2,0.5],[0.5,0.5],[0.8,0.5]]}
          ]
        }
        """.trimIndent()
    )
}
