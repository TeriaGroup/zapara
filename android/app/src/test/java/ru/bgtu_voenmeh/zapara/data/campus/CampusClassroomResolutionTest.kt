package ru.bgtu_voenmeh.zapara.data.campus

import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class CampusClassroomResolutionTest {
    private fun bundled() = CampusGraph.load(File("src/main/assets/maps/campus-graph.json").readText())

    @Test fun classroom_notation_resolves_to_the_correct_place() {
        val graph = bundled()
        val cases = listOf(
            "401;" to "gk.room.401",
            " 401*; " to "ulk.room.401",
            "219А;" to "gk.room.219a",
            "219А*;" to "ulk.room.219а",
            "ВЦ 280;" to "gk.room.280",
            "вц280;" to "gk.room.280",
            "ВЦ КЕ1;" to "gk.room.vcke1",
            "ulk.room.319" to "ulk.room.319"
        )
        for ((raw, expectedId) in cases) {
            assertEquals(raw, expectedId, CampusRouter.resolveClassroom(graph, raw)?.id)
        }
    }

    @Test fun missing_classroom_does_not_fall_back_to_another_building_or_suffix() {
        val graph = bundled()
        for (raw in listOf("320", "493*", "219б*", "дистанционно")) {
            assertNull(raw, CampusRouter.resolveClassroom(graph, raw))
        }
    }

    @Test fun duplicate_room_labels_require_an_explicit_node_id() {
        val graph = CampusGraph(1, listOf("УЛК"), listOf(
            Node("west", "room", "УЛК", 3, 0.1, 0.1, room = "319"),
            Node("east", "room", "УЛК", 3, 0.9, 0.1, room = "319")
        ), emptyList())

        assertNull(CampusRouter.resolveClassroom(graph, "319*"))
        assertEquals("east", CampusRouter.resolveClassroom(graph, "east")?.id)
    }
}
