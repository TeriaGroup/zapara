package ru.bgtu_voenmeh.zapara.ui.maps

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import ru.bgtu_voenmeh.zapara.data.GROUP_FIXTURE
import ru.bgtu_voenmeh.zapara.data.GroupParser
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import ru.bgtu_voenmeh.zapara.data.Schedule
import ru.bgtu_voenmeh.zapara.data.campus.Leg
import ru.bgtu_voenmeh.zapara.data.campus.Route
import ru.bgtu_voenmeh.zapara.ui.XmlCopy
import java.time.LocalDate
import java.time.LocalDateTime

class MapsComposerTest {
    private val parsed by lazy { GroupParser.parse(GROUP_FIXTURE) }
    private val ctx = SchedCtx("3313", LocalDate.of(2026, 9, 1), 2, false)

    private fun lessonsOn(date: LocalDate) =
        Schedule.lessonsForDate(parsed.lessons, ctx.groupId, date, ctx.periodStart, ctx.weekCount, ctx.invert)

    @Test fun zoom_controls_sit_on_an_opaque_toolbar_not_on_the_plan() {
        val section = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/maps/MapsSection.kt").readText()
        assertTrue(section.contains("Maps.ZoomIn"))
        assertTrue(
            "zoom cluster must have an opaque surface so Fit does not sit on the raster",
            section.contains("Maps.ZoomBar") || section.contains("tag = \"Maps.ZoomBar\"")
        )
        val plus = File("src/main/res/drawable/ic_plus.xml").readText()
        assertTrue("plus vertical", plus.contains("M12 5v14"))
        assertTrue("plus horizontal", plus.contains("M5 12h14"))
        assertTrue(
            "Android drops a second move in one path; plus must be two path elements",
            Regex("<path").findAll(plus).count() >= 2
        )
    }

    @Test fun floors_by_building() {
        assertEquals((1..4).toList(), MapsComposer.floors("ГК"))
        assertEquals((1..5).toList(), MapsComposer.floors("УЛК"))
    }

    @Test fun highlight_follows_dest_when_the_shown_floor_changes() {
        val dest = ru.bgtu_voenmeh.zapara.data.campus.Node("gk.493", "room", "ГК", 4, 0.0, 0.0, room = "493")
        val from = ru.bgtu_voenmeh.zapara.data.campus.Node("gk.entrance.main", "entrance", "ГК", 1, 0.0, 0.0)
        assertEquals("493", MapsComposer.highlightRoom("ГК", 4, dest, from))
        assertNull(MapsComposer.highlightRoom("ГК", 2, dest, from))
        val fromRoom = from.copy(id = "gk.101", kind = "room", room = "101")
        assertEquals("101", MapsComposer.highlightRoom("ГК", 1, dest, fromRoom))
        val vm = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/maps/MapsViewModel.kt").readText()
        assertTrue("pickFloor must keep dest chip via highlightRoom", vm.contains("highlightRoom"))
        assertFalse(
            "applyManual must not wipe the dest chip",
            vm.contains("highlight = null, mode = MapMode.Manual")
        )
    }

    @Test fun visible_steps_keep_the_active_floor_in_a_short_window() {
        val steps = listOf(
            RouteStepUi("a", "ГК", 1),
            RouteStepUi("b", "ГК", 2),
            RouteStepUi("c", "ГК", 3),
            RouteStepUi("d", "ГК", 3),
            RouteStepUi("e", "ГК", 4)
        )
        assertEquals(
            listOf(steps[3], steps[4]),
            MapsComposer.visibleSteps(steps, "ГК", 4, max = 2)
        )
        assertEquals(steps.take(2), MapsComposer.visibleSteps(steps, "ГК", 1, max = 2))
        assertEquals(steps, MapsComposer.visibleSteps(steps, "ГК", 4, max = 10))
    }

    @Test fun route_steps_group_consecutive_walks_on_the_same_floor() {
        val route = Route(18, listOf(walkLeg(), walkLeg(), walkLeg()))

        assertEquals(
            listOf(RouteStepUi("Пройдите по коридору (1 этаж)", "ГК", 1)),
            MapsComposer.formatRouteSteps(route, XmlCopy)
        )
    }

    @Test fun route_steps_keep_walk_runs_separate_across_stairs() {
        val route = Route(60, listOf(
            walkLeg(), walkLeg(),
            Leg("stair_up", "ГК", 1, "ГК", 2, emptyList()),
            Leg("stair_down", "ГК", 2, "ГК", 1, emptyList()),
            walkLeg(), walkLeg()
        ))

        assertEquals(
            listOf(
                RouteStepUi("Пройдите по коридору (1 этаж)", "ГК", 1),
                RouteStepUi("Поднимитесь на 2 этаж", "ГК", 2),
                RouteStepUi("Спуститесь на 1 этаж", "ГК", 1),
                RouteStepUi("Пройдите по коридору (1 этаж)", "ГК", 1)
            ),
            MapsComposer.formatRouteSteps(route, XmlCopy)
        )
    }

    @Test fun route_steps_do_not_group_walks_in_different_buildings() {
        val route = Route(12, listOf(walkLeg(), walkLeg(building = "УЛК")))

        assertEquals(
            listOf(
                RouteStepUi("Пройдите по коридору (1 этаж)", "ГК", 1),
                RouteStepUi("Пройдите по коридору (1 этаж)", "УЛК", 1)
            ),
            MapsComposer.formatRouteSteps(route, XmlCopy)
        )
    }

    @Test fun route_steps_do_not_group_walks_on_different_floors() {
        val route = Route(12, listOf(walkLeg(), walkLeg(floor = 2)))

        assertEquals(
            listOf(
                RouteStepUi("Пройдите по коридору (1 этаж)", "ГК", 1),
                RouteStepUi("Пройдите по коридору (2 этаж)", "ГК", 2)
            ),
            MapsComposer.formatRouteSteps(route, XmlCopy)
        )
    }

    private fun walkLeg(building: String = "ГК", floor: Int = 1) =
        Leg("walk", building, floor, null, null, emptyList())

    @Test fun context_line_going_next_tomorrow_and_none() {
        val lesson = Lesson(
            timeStart = "09:00", timeEnd = "10:35",
            roomRaw = "493", buildingRaw = "ГК", classroomRaw = "493;"
        )
        val nowGoing = LocalDateTime.of(2026, 9, 7, 9, 30)
        assertEquals(
            "Идёт пара · 493 ГК · до 10:35",
            MapsComposer.contextLine(nowGoing, listOf(lesson), null, XmlCopy)
        )
        val nowNext = LocalDateTime.of(2026, 9, 7, 8, 20)
        assertEquals(
            "Следующая пара · 493 ГК · через 40 мин",
            MapsComposer.contextLine(nowNext, listOf(lesson), LocalDate.of(2026, 9, 7) to lesson, XmlCopy)
        )
        val nowEve = LocalDateTime.of(2026, 9, 7, 20, 0)
        assertEquals(
            "Следующая пара · завтра 09:00 · 493 ГК",
            MapsComposer.contextLine(nowEve, emptyList(), LocalDate.of(2026, 9, 8) to lesson, XmlCopy)
        )
        assertEquals("Нет предстоящих занятий", MapsComposer.contextLine(nowEve, emptyList(), null, XmlCopy))
    }

    @Test fun next_lesson_skips_sunday_and_finds_monday() {
        val saturday = LocalDateTime.of(2026, 9, 12, 18, 0)
        val next = MapsComposer.nextLesson(saturday, ::lessonsOn, horizonDays = 14)
        assertEquals(LocalDate.of(2026, 9, 14), next?.first)
        assertEquals("лек ВЫСШ. МАТЕМАТ", next?.second?.subjectRaw)
        val far = LocalDateTime.of(2026, 10, 1, 12, 0)
        assertNull(MapsComposer.nextLesson(far, { emptyList() }, horizonDays = 2))
    }

    @Test fun shown_plan_uses_graph_node_floor_and_room() {
        val node = ru.bgtu_voenmeh.zapara.data.campus.Node(
            "gk.room.vcke1", "room", "ГК", 2, 0.5, 0.5, room = "ВЦ КЕ1"
        )
        val plan = MapsComposer.shownPlan("ВЦ", 1, "1", node)
        assertEquals("ГК", plan.building)
        assertEquals(2, plan.floor)
        assertEquals("ВЦ КЕ1", plan.roomRaw)
        val fallback = MapsComposer.shownPlan("ГК", 2, "219А", null)
        assertEquals("ГК", fallback.building)
        assertEquals(2, fallback.floor)
        assertEquals("219А", fallback.roomRaw)
    }

    @Test fun start_fallback_copy_names_the_entrance_used() {
        assertEquals(
            "От выбранного места пройти нельзя. Маршрут от: Вход УЛК",
            MapsComposer.startFallbackMessage("Вход УЛК", XmlCopy)
        )
    }

    @Test fun places_search_finds_rooms_and_entrances() {
        val graph = ru.bgtu_voenmeh.zapara.data.campus.CampusGraph(
            1, listOf("ГК", "УЛК"),
            listOf(
                ru.bgtu_voenmeh.zapara.data.campus.Node("gk.entrance.main", "entrance", "ГК", 1, 0.1, 0.9, label = "Вход ГК"),
                ru.bgtu_voenmeh.zapara.data.campus.Node("ulk.room.564", "room", "УЛК", 5, 0.4, 0.4, room = "564"),
                ru.bgtu_voenmeh.zapara.data.campus.Node("gk.j", "junction", "ГК", 1, 0.2, 0.2)
            ),
            emptyList()
        )
        val places = MapsComposer.places(graph)
        assertEquals(listOf("gk.entrance.main", "ulk.room.564"), places.map { it.id })
        assertEquals("Вход ГК", places[0].label)
        assertEquals("564 · УЛК", places[1].label)
        assertEquals("УЛК, 5", places[1].hint)
        assertEquals(listOf("ulk.room.564"), MapsComposer.filterPlaces(places, "564").map { it.id })
        assertEquals(listOf("gk.entrance.main"), MapsComposer.filterPlaces(places, "вход").map { it.id })
        assertEquals(places, MapsComposer.filterPlaces(places, "  "))
    }

    @Test fun picker_without_query_keeps_entrances_and_rooms_of_the_shown_floor() {
        val places = listOf(
            RoutePlaceUi("gk.entrance.main", "Вход ГК", "1-й этаж", "entrance", "ГК", 1, "вход гк"),
            RoutePlaceUi("ulk.entrance.main", "Вход УЛК", "1-й этаж", "entrance", "УЛК", 1, "вход улк"),
            RoutePlaceUi("ulk.room.101", "101 · УЛК", "1-й этаж", "room", "УЛК", 1, "101 улк"),
            RoutePlaceUi("ulk.room.564", "564 · УЛК", "5-й этаж", "room", "УЛК", 5, "564 улк"),
            RoutePlaceUi("gk.room.101", "101 · ГК", "1-й этаж", "room", "ГК", 1, "101 гк")
        )
        assertEquals(
            listOf("ulk.entrance.main", "ulk.room.101"),
            MapsComposer.pickerItems(places, "", "УЛК", 1).map { it.id }
        )
        assertEquals(
            listOf("ulk.room.564"),
            MapsComposer.pickerItems(places, "564", "УЛК", 1).map { it.id }
        )
        assertEquals(
            listOf("gk.entrance.main", "ulk.entrance.main", "ulk.room.101", "ulk.room.564", "gk.room.101"),
            MapsComposer.pickerItems(places, "", null, null).map { it.id }
        )
    }

    @Test fun duration_label_rounds_to_minutes() {
        assertEquals("меньше минуты", MapsComposer.durationLabel(45.0, XmlCopy))
        assertEquals("около 1 мин", MapsComposer.durationLabel(60.0, XmlCopy))
        assertEquals("около 3 мин", MapsComposer.durationLabel(155.0, XmlCopy))
        assertNull(MapsComposer.durationLabel(0.0, XmlCopy))
    }

    @Test fun smallest_overlapping_room_wins_the_plan_hit() {
        val wide = FloorRoom("wide", "101", ru.bgtu_voenmeh.zapara.data.CoordsRect(0.30, 0.20, 0.20, 0.20))
        val door = FloorRoom("door", "гардероб", ru.bgtu_voenmeh.zapara.data.CoordsRect(0.38, 0.28, 0.04, 0.04))
        assertEquals("door", MapsComposer.hitRoom(0.40, 0.30, listOf(wide, door))?.id)
        assertEquals("wide", MapsComposer.hitRoom(0.32, 0.22, listOf(wide, door))?.id)
        assertNull(MapsComposer.hitRoom(0.10, 0.10, listOf(wide, door)))
    }

    @Test fun floor_rooms_pair_graph_nodes_with_coords_on_that_plan() {
        val graph = ru.bgtu_voenmeh.zapara.data.campus.CampusGraph(
            1, listOf("УЛК"),
            listOf(
                ru.bgtu_voenmeh.zapara.data.campus.Node("ulk.room.101", "room", "УЛК", 1, 0.37, 0.28, room = "101"),
                ru.bgtu_voenmeh.zapara.data.campus.Node("ulk.room.564", "room", "УЛК", 5, 0.4, 0.4, room = "564"),
                ru.bgtu_voenmeh.zapara.data.campus.Node("ulk.j", "junction", "УЛК", 1, 0.2, 0.2)
            ),
            emptyList()
        )
        val coords = mapOf(
            "101" to ru.bgtu_voenmeh.zapara.data.CoordsRect(0.35, 0.26, 0.055, 0.042),
            "564" to ru.bgtu_voenmeh.zapara.data.CoordsRect(0.4, 0.4, 0.05, 0.04)
        )
        val rooms = MapsComposer.floorRooms(graph, "УЛК", 1, coords)
        assertEquals(listOf("ulk.room.101"), rooms.map { it.id })
        assertEquals("101", rooms.single().room)
    }
}
