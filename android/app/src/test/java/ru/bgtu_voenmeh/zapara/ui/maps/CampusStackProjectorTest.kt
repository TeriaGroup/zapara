package ru.bgtu_voenmeh.zapara.ui.maps

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.MapResolve
import ru.bgtu_voenmeh.zapara.data.campus.CampusGraph
import ru.bgtu_voenmeh.zapara.data.campus.CampusRouter
import ru.bgtu_voenmeh.zapara.ui.XmlCopy
import java.io.File
import kotlin.math.abs

class CampusStackProjectorTest {
    @Test fun overview_fit_keeps_all_five_quads_inside_short_canvas_after_orbit() {
        val scene = CampusStackProjector.project(null, "УЛК", 400f, 160f, yaw = 1.4f, pitch = 1.2f)
            .fitOverview(400f, 160f, 8f)
        assertTrue(scene.floors.flatMap { it.points2 }.all { it.x in 8.0..392.0 && it.y in 8.0..152.0 })
    }
    @Test fun presentation_keeps_missing_floor_quad_but_filters_unavailable_ink() {
        val p = overviewPresentation("walk", "ГК", 1, "ГК", 1)
        val scene = CampusStackProjector.project(p, "ГК", 400f, 400f, floors = listOf(1, 2))
        assertEquals(listOf(1, 2), scene.floors.map { it.floor })
        assertEquals(1, scene.polylines.size)
        assertTrue(scene.withRasters(setOf(2)).polylines.isEmpty())
        assertEquals(scene.floors, scene.withRasters(emptySet()).floors)
        assertTrue(CampusStackProjector.project(p, "ГК", 400f, 400f, floors = emptyList()).polylines.isEmpty())
    }

    @Test fun presentation_links_are_local_markers_never_cross_building_lines_in_either_direction() {
        for ((from, to) in listOf("ГК" to "УЛК", "УЛК" to "ГК")) {
            val p = overviewPresentation("building_link", from, 1, to, 2)
            val departure = CampusStackProjector.project(p, from, 400f, 400f, floors = listOf(1, 2))
            val arrival = CampusStackProjector.project(p, to, 400f, 400f, floors = listOf(1, 2))
            assertTrue(departure.polylines.isEmpty())
            assertTrue(arrival.polylines.isEmpty())
            assertEquals(RouteMarkerKind.LinkDeparture, departure.markers.single().marker.kind)
            assertEquals(RouteMarkerKind.LinkArrival, arrival.markers.single().marker.kind)
            assertEquals(from, departure.markers.single().marker.floor.building)
            assertEquals(to, arrival.markers.single().marker.floor.building)
            assertTrue(departure.withRasters(emptySet()).markers.isEmpty())
        }
    }

    @Test fun presentation_stairs_use_actual_paired_markers_and_reverse_z() {
        for ((kind, levels) in listOf("stair_up" to (1 to 3), "stair_down" to (3 to 1))) {
            val p = overviewPresentation(kind, "УЛК", levels.first, "УЛК", levels.second)
            val scene = CampusStackProjector.project(p, "УЛК", 400f, 400f, floors = listOf(1, 2, 3))
            val line = scene.polylines.single()
            assertEquals(kind, line.kind)
            assertEquals(levels.first.toDouble(), line.points.first().z, 0.0)
            assertEquals(levels.second.toDouble(), line.points.last().z, 0.0)
            assertTrue(line.points.all { it.x == 0.2 && it.y == 0.4 })
            assertTrue(scene.withRasters(setOf(1)).polylines.isEmpty())
            assertTrue(CampusStackProjector.project(p, "ГК", 400f, 400f).polylines.isEmpty())
            val unpaired = p.copy(steps = p.steps.map { it.copy(markers = it.markers.take(1)) })
            assertTrue(CampusStackProjector.project(unpaired, "УЛК", 400f, 400f).polylines.isEmpty())
        }
    }

    @Test fun presentation_rejects_invalid_geometry_without_bridging_and_keeps_paint_order() {
        val p = overviewPresentation("walk", "ГК", 1, "ГК", 1)
        val bad = p.copy(segments = p.segments.map { it.copy(points = listOf(
            ru.bgtu_voenmeh.zapara.data.campus.GraphPoint(0.0, 0.0),
            ru.bgtu_voenmeh.zapara.data.campus.GraphPoint(Double.NaN, 0.5),
            ru.bgtu_voenmeh.zapara.data.campus.GraphPoint(1.0, 1.0))) })
        assertTrue(CampusStackProjector.project(bad, "ГК", 400f, 400f).polylines.isEmpty())
        val scene = CampusStackProjector.project(p, "ГК", 400f, 400f, floors = listOf(1, 2))
        assertEquals(listOf("floor", "walk", "floor"), CampusStackProjector.paintSequence(scene).map { it.kind })
    }

    private fun overviewPresentation(kind: String, building: String, floor: Int, target: String, targetFloor: Int): RoutePresentation {
        val point = ru.bgtu_voenmeh.zapara.data.campus.GraphPoint(0.2, 0.4)
        val end = if (kind.startsWith("stair")) point else ru.bgtu_voenmeh.zapara.data.campus.GraphPoint(0.8, 0.6)
        val route = ru.bgtu_voenmeh.zapara.data.campus.Route(10, listOf(
            ru.bgtu_voenmeh.zapara.data.campus.Leg(kind, building, floor, target, targetFloor, listOf(point, end))))
        return RoutePresentationBuilder.build(route, null, null, emptyMap())
    }

    @Test fun labyrinth_west3_to_east3_is_down_across_up_not_floor3_shortcut() {
        val route = CampusRouter.find(labyrinth(), "lab.room.west.3", "lab.room.east.3").route!!
        val lines = CampusStackProjector.routePolylines(route, "УЛК")
        assertTrue(lines.any { isDescending(it) })
        assertTrue(lines.any { it.kind == "walk" && it.floor == 1 && isHorizontal(it) && it.points.any { p -> p.x == 0.5 && p.y == 0.5 } })
        assertTrue(lines.any { isAscending(it) })
        val floor3 = lines.filter { it.kind == "walk" && it.floor == 3 }
        assertEquals(2, floor3.size)
        assertFalse(floor3.any { spansWestToEastOnFloor3(it) })
    }

    @Test fun lower_floor_walks_are_painted_before_higher_floor_rasters() {
        val route = CampusRouter.find(labyrinth(), "lab.room.west.3", "lab.room.east.3").route!!
        val scene = CampusStackProjector.project(route, "УЛК", 400f, 400f)
        val seq = CampusStackProjector.paintSequence(scene)
        val walk1 = seq.indexOfFirst { it.kind == "walk" && it.floor == 1 }
        val floor3 = seq.indexOfFirst { it.kind == "floor" && it.floor == 3 }
        assertTrue("walk1=$walk1 floor3=$floor3", walk1 >= 0 && floor3 >= 0)
        assertTrue("floor-1 corridor is drawn on top of floor 3", walk1 < floor3)
    }

    @Test fun summary_section_paints_an_opaque_canvas() {
        val src = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/summary/SummarySection.kt").readText()
        assertTrue(src.contains("background"))
        assertTrue(src.contains("c.canvas") || src.contains("Zapara.colors.canvas"))
    }

    @Test fun teachers_show_skeleton_until_the_roster_loads() {
        val src = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/teachers/TeachersSection.kt").readText()
        assertTrue(src.contains("SkeletonList"))
        assertTrue(src.contains("!state.loaded") || src.contains("state.loaded.not()"))
    }

    @Test fun empty_route_has_no_path_polylines() {
        assertTrue(CampusStackProjector.routePolylines(null, "УЛК").isEmpty())
        val empty = CampusGraph.load("""{"version":1,"buildings":["ГК","УЛК"],"nodes":[],"edges":[]}""")
        val result = CampusRouter.find(empty, "a", "b")
        assertTrue(CampusStackProjector.routePolylines(result.route, "ГК").isEmpty())
    }

    @Test fun stair_segments_use_shaft_xy_and_floor_as_z() {
        val route = CampusRouter.find(labyrinth(), "lab.room.west.3", "lab.room.east.3").route!!
        val stairs = CampusStackProjector.routePolylines(route, "УЛК").filter { it.kind == "stair_up" || it.kind == "stair_down" }
        assertTrue(stairs.isNotEmpty())
        for (s in stairs) {
            assertEquals(2, s.points.size)
            assertEquals(s.floor.toDouble(), s.points[0].z, 1e-6)
            assertEquals(s.toFloor!!.toDouble(), s.points[1].z, 1e-6)
            assertEquals(s.points[0].x, s.points[1].x, 1e-6)
            assertEquals(s.points[0].y, s.points[1].y, 1e-6)
        }
        assertTrue(stairs.any { it.points[0].x == 0.2 && it.points[0].y == 0.5 && it.points[0].z > it.points[1].z })
        assertTrue(stairs.any { it.points[0].x == 0.8 && it.points[0].y == 0.5 && it.points[0].z < it.points[1].z })
    }

    @Test fun project_is_static_by_default_and_yaw_changes_2d_not_3d() {
        val route = CampusRouter.find(labyrinth(), "lab.room.west.3", "lab.room.east.3").route!!
        val a = CampusStackProjector.project(route, "УЛК", 400f, 300f)
        val b = CampusStackProjector.project(route, "УЛК", 400f, 300f)
        assertEquals(a.polylines.flatMap { it.points2 }, b.polylines.flatMap { it.points2 })
        val spun = CampusStackProjector.project(route, "УЛК", 400f, 300f, yaw = CampusStackProjector.DefaultYaw + 0.4f)
        assertNotEquals(a.polylines.flatMap { it.points2 }, spun.polylines.flatMap { it.points2 })
        assertEquals(a.polylines.flatMap { it.points }, spun.polylines.flatMap { it.points })
    }

    @Test fun higher_floor_projects_above_lower_floor() {
        val scene = CampusStackProjector.project(null, "УЛК", 400f, 400f)
        val y1 = scene.floors.single { it.floor == 1 }.points2.map { it.y }.average()
        val y3 = scene.floors.single { it.floor == 3 }.points2.map { it.y }.average()
        assertTrue("floor 3 y=$y3 should sit above floor 1 y=$y1", y3 < y1)
        assertTrue(scene.polylines.isEmpty())
    }

    @Test fun floor_rasters_cover_each_bundled_floor_of_ulk() {
        val files = MapsComposer.floors("УЛК").associateWith { n ->
            File("src/main/assets/maps/${MapResolve.MAP_FILES.getValue("УЛК" to n)}")
        }
        assertTrue("bundled УЛК plans should be on disk", files.values.count { it.isFile } >= 2)
        val rasters = CampusStackProjector.floorRasters("УЛК") { n -> files[n]?.takeIf { it.isFile } }
        assertTrue("stack model must get more than one floor raster", rasters.size >= 2)
        assertEquals(files.filter { it.value.isFile }.keys, rasters.keys)
        assertTrue(rasters.containsKey(1) && rasters.containsKey(5))
    }

    @Test fun map_stack_copy_is_schema_etazhey() {
        assertEquals("Схема этажей", XmlCopy.get("maps_stack"))
    }

    @Test fun stack_view_has_no_engine_or_auto_orbit() {
        val view = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/maps/CampusStack.kt").readText()
        val projector = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/maps/CampusStackProjector.kt").readText()
        val src = view + projector
        assertFalse(src.contains("Unity"))
        assertFalse(src.contains("OpenGL"))
        assertFalse(src.contains("unreal", ignoreCase = true))
        assertFalse(src.contains("rememberInfiniteTransition"))
        assertFalse(src.contains("LaunchedEffect"))
        assertTrue(view.contains("Zapara.motion.enabled"))
        assertTrue(view.contains("CampusStackProjector"))
        assertTrue(view.contains("PathGeometry.at"))
        assertTrue(view.contains("drawCircle"))
        assertTrue(view.contains("detectDragGestures"))
        val section = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/maps/MapsSection.kt").readText()
        assertTrue(section.contains("maps_stack"))
        assertTrue(section.contains("showStack"))
        val vm = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/maps/MapsViewModel.kt").readText()
        assertTrue(vm.contains("ToggleStack"))
        assertTrue(vm.contains("showStack = !it.showStack"))
        assertTrue(view.contains("floorFiles"))
        assertTrue(view.contains("paintSequence"))
        assertFalse(view.contains("quad.floor == imageFloor"))
        assertTrue(section.contains("floorFiles"))
    }

    private fun isDescending(line: StackPolyline) =
        line.points.zipWithNext().any { (a, b) -> a.z > b.z }

    private fun isAscending(line: StackPolyline) =
        line.points.zipWithNext().any { (a, b) -> a.z < b.z }

    private fun isHorizontal(line: StackPolyline) =
        line.points.size >= 2 && line.points.all { abs(it.z - line.points[0].z) < 1e-9 }

    private fun spansWestToEastOnFloor3(line: StackPolyline): Boolean {
        if (line.points.isEmpty()) return false
        return line.points.minOf { it.x } <= 0.25 && line.points.maxOf { it.x } >= 0.75
    }

    private fun labyrinth() = CampusGraph.load(
        """
        {
          "version": 1,
          "buildings": ["УЛК"],
          "nodes": [
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
