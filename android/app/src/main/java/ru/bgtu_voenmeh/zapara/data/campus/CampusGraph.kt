package ru.bgtu_voenmeh.zapara.data.campus

import ru.bgtu_voenmeh.zapara.data.api.JsonFail
import ru.bgtu_voenmeh.zapara.data.api.JsonValue
import ru.bgtu_voenmeh.zapara.data.api.StrictJson
import ru.bgtu_voenmeh.zapara.data.api.arr
import ru.bgtu_voenmeh.zapara.data.api.bool
import ru.bgtu_voenmeh.zapara.data.api.field
import ru.bgtu_voenmeh.zapara.data.api.int
import ru.bgtu_voenmeh.zapara.data.api.obj
import ru.bgtu_voenmeh.zapara.data.api.text

data class GraphPoint(val x: Double, val y: Double)

data class Node(
    val id: String,
    val kind: String,
    val building: String,
    val floor: Int,
    val x: Double,
    val y: Double,
    val label: String? = null,
    val room: String? = null,
    val group: String? = null
)

data class Edge(
    val from: String,
    val to: String,
    val kind: String,
    val seconds: Double,
    val oneWay: Boolean,
    val points: List<GraphPoint>? = null
)

data class BlockedRegion(
    val building: String,
    val floor: Int,
    val left: Double,
    val top: Double,
    val right: Double,
    val bottom: Double,
    val ownerId: String? = null
)

data class CampusGraph(
    val version: Int,
    val buildings: List<String>,
    val nodes: List<Node>,
    val edges: List<Edge>,
    val blocked: List<BlockedRegion> = emptyList()
) {
    companion object {
        val empty: CampusGraph by lazy {
            load("""{"version":1,"buildings":["ГК","УЛК"],"nodes":[],"edges":[]}""")
        }

        fun load(json: String): CampusGraph = CampusGraphJson.load(json)
    }
}

data class Leg(
    val kind: String,
    val building: String,
    val floor: Int,
    val toBuilding: String?,
    val toFloor: Int?,
    val points: List<GraphPoint>
)

data class Route(val seconds: Int, val legs: List<Leg>)

data class RouteResult(val ok: Boolean, val route: Route?, val failure: String?) {
    companion object {
        fun success(route: Route) = RouteResult(true, route, null)
        fun fail(failure: String) = RouteResult(false, null, failure)
    }
}

class CampusGraphException(val code: String, message: String) : RuntimeException(message)

private object CampusGraphJson {
    private val allowedBuildings = setOf("ГК", "УЛК")
    private val nodeKinds = setOf("entrance", "room", "stair", "landing", "junction", "building_link")
    private val edgeKinds = setOf("walk", "stair_up", "stair_down", "building_link")
    private val verticalKinds = setOf("stair", "landing")

    fun load(json: String): CampusGraph {
        if (json.isBlank()) throw invalidJson()
        val root = try {
            StrictJson.parse(json).obj()
        } catch (_: JsonFail) {
            throw invalidJson()
        }
        try {
            if (root.int("version") != 1) throw invalidJson()
            val buildingsEl = root.array("buildings")
            val buildings = ArrayList<String>(buildingsEl.items.size)
            val buildingSet = LinkedHashSet<String>()
            for (item in buildingsEl.items) {
                val name = (item as? JsonValue.Str)?.value ?: throw invalidJson()
                if (name !in allowedBuildings || !buildingSet.add(name)) throw invalidJson()
                buildings.add(name)
            }
            val nodesEl = root.array("nodes")
            val nodes = ArrayList<Node>(nodesEl.items.size)
            val byId = LinkedHashMap<String, Node>()
            for (item in nodesEl.items) {
                val node = readNode(item.obj(), buildingSet)
                if (byId.put(node.id, node) != null) throw invalidJson()
                nodes.add(node)
            }
            val edgesEl = root.array("edges")
            val edges = ArrayList<Edge>(edgesEl.items.size)
            for (item in edgesEl.items) edges.add(readEdge(item.obj(), byId))
            val blocked = readBlocked(root, buildingSet, byId)
            return CampusGraph(1, buildings, nodes, edges, blocked)
        } catch (ex: CampusGraphException) {
            throw ex
        } catch (_: JsonFail) {
            throw invalidJson()
        }
    }

    private fun readNode(el: JsonValue.Obj, buildings: Set<String>): Node {
        val id = el.text("id")
        if (id.isBlank()) throw invalidJson()
        val kind = el.text("kind")
        if (kind !in nodeKinds) throw invalidJson()
        val building = el.text("building")
        if (building !in buildings) throw invalidJson()
        val floor = el.int("floor")
        if (floor !in 1..5) throw invalidJson()
        val x = el.number("x")
        val y = el.number("y")
        if (x !in 0.0..1.0 || y !in 0.0..1.0) throw invalidJson()
        return Node(id, kind, building, floor, x, y, el.optText("label"), el.optText("room"), el.optText("group"))
    }

    private fun readEdge(el: JsonValue.Obj, byId: Map<String, Node>): Edge {
        val fromId = el.text("from")
        val toId = el.text("to")
        val kind = el.text("kind")
        if (kind !in edgeKinds) throw invalidJson()
        val seconds = el.number("seconds")
        if (seconds <= 0.0 || !seconds.isFinite()) throw invalidJson()
        val oneWay = el.bool("oneWay")
        val points = readPoints(el)
        val from = byId[fromId]
        val to = byId[toId]
        if (from == null || to == null) {
            throw CampusGraphException("unknown_node", "Ребро ссылается на неизвестный узел.")
        }
        when (kind) {
            "walk" -> if (from.building != to.building || from.floor != to.floor) {
                throw CampusGraphException("bad_edge_floor", "Пешеходное ребро должно соединять узлы одного корпуса и этажа.")
            }
            "stair_up", "stair_down" -> validateStair(from, to, kind)
            "building_link" -> if (from.kind != "building_link" || to.kind != "building_link") throw invalidJson()
        }
        validateGeometry(points, from, to)
        return Edge(fromId, toId, kind, seconds, oneWay, points)
    }

    private fun validateStair(from: Node, to: Node, kind: String) {
        if (from.building != to.building) throw badStair()
        if (from.kind !in verticalKinds || to.kind !in verticalKinds) throw badStair()
        if (from.group.isNullOrBlank() || from.group != to.group) throw badStair()
        val delta = to.floor - from.floor
        if (kotlin.math.abs(delta) != 1) throw badStair()
        if (kind == "stair_up" && delta != 1) throw badStair()
        if (kind == "stair_down" && delta != -1) throw badStair()
    }

    private fun validateGeometry(points: List<GraphPoint>?, from: Node, to: Node) {
        // Missing/empty geometry retains the node-to-node fallback.
        if (points.isNullOrEmpty()) return
        fun matches(point: GraphPoint, node: Node) =
            kotlin.math.abs(point.x - node.x) <= 0.000001 && kotlin.math.abs(point.y - node.y) <= 0.000001
        // A singleton can only connect two nodes at the same position.
        if (!matches(points.first(), from) || !matches(points.last(), to)) {
            throw CampusGraphException("bad_edge_geometry", "Линия ребра должна соединять координаты его узлов.")
        }
    }

    private fun readBlocked(
        root: JsonValue.Obj,
        buildings: Set<String>,
        byId: Map<String, Node>
    ): List<BlockedRegion> {
        val raw = root.fields["blocked"] ?: return emptyList()
        if (raw is JsonValue.Null) return emptyList()
        val arr = raw as? JsonValue.Arr ?: throw invalidJson()
        val blocked = ArrayList<BlockedRegion>(arr.items.size)
        for (item in arr.items) {
            val el = item as? JsonValue.Obj ?: throw invalidJson()
            val building = el.text("building")
            if (building !in buildings) throw invalidJson()
            val floor = el.int("floor")
            if (floor !in 1..5) throw invalidJson()
            val left = el.number("left")
            val top = el.number("top")
            val right = el.number("right")
            val bottom = el.number("bottom")
            if (left !in 0.0..1.0 || top !in 0.0..1.0 || right !in 0.0..1.0 || bottom !in 0.0..1.0
                || left >= right || top >= bottom) throw invalidJson()
            val owner = el.optText("owner")
            if (owner != null && owner !in byId) throw invalidJson()
            blocked.add(BlockedRegion(building, floor, left, top, right, bottom, owner))
        }
        return blocked
    }

    private fun readPoints(el: JsonValue.Obj): List<GraphPoint>? {
        val raw = el.fields["points"] ?: return null
        if (raw is JsonValue.Null) return null
        val arr = raw as? JsonValue.Arr ?: throw invalidJson()
        val points = ArrayList<GraphPoint>(arr.items.size)
        for (item in arr.items) {
            val pair = item as? JsonValue.Arr ?: throw invalidJson()
            if (pair.items.size != 2) throw invalidJson()
            val x = pair.items[0].finiteNumber()
            val y = pair.items[1].finiteNumber()
            if (x !in 0.0..1.0 || y !in 0.0..1.0) throw invalidJson()
            points.add(GraphPoint(x, y))
        }
        return points
    }

    private fun JsonValue.Obj.array(name: String): JsonValue.Arr {
        val v = field(name).arr()
        return v
    }

    private fun JsonValue.Obj.number(name: String): Double = field(name).finiteNumber()

    private fun JsonValue.Obj.optText(name: String): String? {
        val v = fields[name] ?: return null
        if (v is JsonValue.Null) return null
        val s = v as? JsonValue.Str ?: throw invalidJson()
        return s.value
    }

    private fun JsonValue.finiteNumber(): Double {
        val n = this as? JsonValue.Num ?: throw invalidJson()
        val v = n.raw.toDoubleOrNull() ?: throw invalidJson()
        if (!v.isFinite()) throw invalidJson()
        return v
    }

    private fun invalidJson() = CampusGraphException("invalid_json", "Некорректный JSON графа кампуса.")
    private fun badStair() = CampusGraphException("bad_stair", "Лестничное ребро должно соединять соседние этажи одной шахты.")
}
