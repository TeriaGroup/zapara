package ru.bgtu_voenmeh.zapara.data.campus

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class CampusGraphValidationTest {
    @Test fun load_reads_blocked_keep_outs_and_routes_around_them() {
        val graph = CampusGraph.load(
            """
            {"version":1,"buildings":["ГК"],"nodes":[
              {"id":"start","kind":"junction","building":"ГК","floor":1,"x":0.1,"y":0.5},
              {"id":"goal","kind":"junction","building":"ГК","floor":1,"x":0.9,"y":0.5},
              {"id":"corridor","kind":"junction","building":"ГК","floor":1,"x":0.5,"y":0.1},
              {"id":"classroom","kind":"room","building":"ГК","floor":1,"x":0.5,"y":0.5,"room":"101"}
            ],"edges":[
              {"from":"start","to":"goal","kind":"walk","seconds":1,"oneWay":false,"points":[[0.1,0.5],[0.5,0.5],[0.9,0.5]]},
              {"from":"start","to":"corridor","kind":"walk","seconds":5,"oneWay":false},
              {"from":"corridor","to":"goal","kind":"walk","seconds":5,"oneWay":false},
              {"from":"corridor","to":"classroom","kind":"walk","seconds":1,"oneWay":false,"points":[[0.5,0.1],[0.5,0.5]]}
            ],"blocked":[
              {"building":"ГК","floor":1,"left":0.4,"top":0.4,"right":0.6,"bottom":0.6,"owner":"classroom"}
            ]}
            """.trimIndent()
        )
        val keepOut = graph.blocked.single()
        assertEquals("ГК", keepOut.building)
        assertEquals(1, keepOut.floor)
        assertEquals(.4, keepOut.left, 0.0)
        assertEquals(.4, keepOut.top, 0.0)
        assertEquals(.6, keepOut.right, 0.0)
        assertEquals(.6, keepOut.bottom, 0.0)
        assertEquals("classroom", keepOut.ownerId)
        assertEquals(10, CampusRouter.find(graph, "start", "goal").route!!.seconds)
        assertTrue(CampusRouter.find(graph, "corridor", "classroom").ok)
    }

    @Test fun matching_stair_group_cannot_connect_different_buildings() {
        val json = """
            {"version":1,"buildings":["ГК","УЛК"],"nodes":[
              {"id":"a","kind":"stair","building":"ГК","floor":1,"x":0.1,"y":0.2,"group":"shared"},
              {"id":"b","kind":"stair","building":"УЛК","floor":2,"x":0.5,"y":0.6,"group":"shared"}
            ],"edges":[{"from":"a","to":"b","kind":"stair_up","seconds":20,"oneWay":false}]}
        """.trimIndent()
        assertEquals("bad_stair", assertThrows(CampusGraphException::class.java) { CampusGraph.load(json) }.code)
    }

    @Test fun blank_stair_group_cannot_identify_a_shaft() {
        val json = """
            {"version":1,"buildings":["ГК"],"nodes":[
              {"id":"a","kind":"stair","building":"ГК","floor":1,"x":0.1,"y":0.2,"group":" "},
              {"id":"b","kind":"landing","building":"ГК","floor":2,"x":0.5,"y":0.6,"group":" "}
            ],"edges":[{"from":"a","to":"b","kind":"stair_up","seconds":20,"oneWay":false}]}
        """.trimIndent()
        assertEquals("bad_stair", assertThrows(CampusGraphException::class.java) { CampusGraph.load(json) }.code)
    }

    @Test fun explicit_polylines_cannot_disappear_or_jump_between_graph_nodes() {
        for (points in listOf("[[0.1,0.2]]", "[[0.2,0.2],[0.5,0.6]]", "[[0.1,0.2],[0.5,0.5]]", "[[0.5,0.6],[0.1,0.2]]")) {
            assertEquals("bad_edge_geometry", assertThrows(CampusGraphException::class.java) { CampusGraph.load(walk(points)) }.code)
        }
    }

    @Test fun unspecified_geometry_keeps_the_endpoint_fallback() {
        for (points in listOf("null", "[]")) {
            val route = CampusRouter.find(CampusGraph.load(walk(points)), "a", "b").route!!
            assertEquals(listOf(GraphPoint(.1, .2), GraphPoint(.5, .6)), route.legs.single().points)
        }
    }

    @Test fun explicit_polylines_allow_subpixel_coordinate_rounding() {
        assertTrue(CampusRouter.find(CampusGraph.load(walk("[[0.1000001,0.2],[0.3,0.4],[0.4999999,0.6]]")), "a", "b").ok)
    }

    @Test fun coincident_connector_nodes_do_not_require_artificial_movement() {
        val graph = CampusGraph.load(walk("[[0.1,0.2]]").replace("\"x\":0.5,\"y\":0.6", "\"x\":0.1,\"y\":0.2"))
        assertTrue(CampusRouter.find(graph, "a", "b").ok)
    }

    private fun walk(points: String) = """
        {"version":1,"buildings":["ГК"],"nodes":[
          {"id":"a","kind":"junction","building":"ГК","floor":1,"x":0.1,"y":0.2},
          {"id":"b","kind":"room","building":"ГК","floor":1,"x":0.5,"y":0.6,"room":"101"}
        ],"edges":[{"from":"a","to":"b","kind":"walk","seconds":10,"oneWay":false,"points":$points}]}
    """.trimIndent()
}
