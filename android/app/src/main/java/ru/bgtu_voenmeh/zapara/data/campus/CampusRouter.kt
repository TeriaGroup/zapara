package ru.bgtu_voenmeh.zapara.data.campus

import java.util.PriorityQueue
import kotlin.math.roundToInt

object CampusRouter {
    fun find(graph: CampusGraph, fromId: String?, toId: String?): RouteResult {
        val byId = LinkedHashMap<String, Node>(graph.nodes.size)
        for (node in graph.nodes) byId.putIfAbsent(node.id, node)
        val start = fromId?.let { byId[it] }
        val goal = toId?.let { byId[it] }
        if (fromId == null || toId == null || start == null || goal == null) {
            return RouteResult.fail("unknown_place")
        }
        if (fromId == toId) return RouteResult.success(Route(0, emptyList()))

        val adj = buildAdjacency(graph)
        val startCost = SearchCost(0.0, 0)
        val gScore = HashMap<String, SearchCost>().apply { put(fromId, startCost) }
        val cameFrom = HashMap<String, CameFrom>()
        // Dijkstra: floor numbers alone cannot bound an authored building link's cost.
        val open = PriorityQueue<Open>(compareBy<Open> { it.cost }.thenBy { it.id })
        open.add(Open(fromId, startCost))
        val closed = HashSet<String>()

        while (true) {
            val currentId = open.poll()?.id ?: break
            if (!closed.add(currentId)) continue
            if (currentId == toId) {
                return RouteResult.success(buildRoute(fromId, toId, byId, cameFrom, gScore[toId]!!.seconds))
            }
            // A room may have several doors, but is a destination rather than a corridor.
            if (currentId != fromId && byId[currentId]!!.kind == "room") continue
            val links = adj[currentId] ?: continue
            val currentG = gScore[currentId]!!
            for (link in links) {
                if (link.to in closed) continue
                val tentative = SearchCost(currentG.seconds + link.edge.seconds,
                    currentG.stairs + if (link.edge.kind == "stair_up" || link.edge.kind == "stair_down") 1 else 0)
                val known = gScore[link.to]
                if (known != null && tentative >= known) continue
                cameFrom[link.to] = CameFrom(currentId, link.edge, link.reverse)
                gScore[link.to] = tentative
                open.add(Open(link.to, tentative))
            }
        }
        return RouteResult.fail("unreachable")
    }

    fun findFromEntrance(graph: CampusGraph, entranceId: String, roomKey: String): RouteResult {
        val entrance = resolveEntrance(graph, entranceId)
        val room = resolve(graph, roomKey)
        if (entrance == null || room == null) return RouteResult.fail("unknown_place")
        return find(graph, entrance.id, room.id)
    }

    fun findResolved(graph: CampusGraph, fromToken: String, toToken: String): RouteResult {
        val from = resolve(graph, fromToken)
        val to = resolve(graph, toToken)
        if (from == null || to == null) return RouteResult.fail("unknown_place")
        return find(graph, from.id, to.id)
    }

    fun roomKey(classroomRaw: String?): String {
        if (classroomRaw.isNullOrBlank()) return ""
        return classroomRaw.trim().trimEnd(';').replace("*", "").trim()
    }

    fun resolve(graph: CampusGraph, token: String?): Node? {
        if (token.isNullOrBlank()) return null
        val key = roomKey(token)
        if (key.isEmpty()) return null
        for (node in graph.nodes) {
            if (node.id == token || node.id == key) return node
        }
        return graph.nodes.singleOrNull { it.room != null && it.room == key }
    }

    /** Schedule notation: an asterisk means УЛК; an unmarked classroom means ГК. */
    fun resolveClassroom(graph: CampusGraph, classroomRaw: String?): Node? {
        if (classroomRaw.isNullOrBlank()) return null
        val raw = classroomRaw.trim().trimEnd(';').trim()
        graph.nodes.firstOrNull { it.id == raw }?.let { return it }
        val building = if (raw.contains("ВЦ", ignoreCase = true) || '*' !in raw) "ГК" else "УЛК"
        val cleaned = roomKey(raw)
        val key = Regex("""^ВЦ\s*([0-9]+[а-яa-z]?)$""", RegexOption.IGNORE_CASE)
            .matchEntire(cleaned)?.groupValues?.get(1) ?: cleaned
        return graph.nodes.singleOrNull {
            it.kind == "room" && it.building == building && it.room.equals(key, ignoreCase = true)
        }
    }

    fun resolveEntrance(graph: CampusGraph, entranceId: String?): Node? {
        if (entranceId.isNullOrBlank()) return null
        for (node in graph.nodes) {
            if (node.id == entranceId && node.kind == "entrance") return node
        }
        return null
    }

    fun chooseFrom(graph: CampusGraph, previousRoomKey: String?, lastEntranceId: String?): Node? {
        resolve(graph, previousRoomKey)?.let { return it }
        return resolveEntrance(graph, lastEntranceId)
    }

    fun entrances(graph: CampusGraph): List<Node> = graph.nodes.filter { it.kind == "entrance" }

    private fun buildAdjacency(graph: CampusGraph): Map<String, List<Link>> {
        val byId = LinkedHashMap<String, Node>(graph.nodes.size)
        for (node in graph.nodes) byId.putIfAbsent(node.id, node)
        val adj = HashMap<String, MutableList<Link>>()
        fun add(from: String, link: Link) {
            adj.getOrPut(from) { ArrayList() }.add(link)
        }
        for (edge in graph.edges) {
            val from = byId[edge.from] ?: continue
            val to = byId[edge.to] ?: continue
            if (walkBlocked(edge, from, to, graph.blocked)) continue
            add(edge.from, Link(edge.to, edge, reverse = false))
            if (!edge.oneWay) add(edge.to, Link(edge.from, edge, reverse = true))
        }
        return adj
    }

    private fun walkBlocked(edge: Edge, from: Node, to: Node, blocked: List<BlockedRegion>): Boolean {
        if (edge.kind != "walk" || blocked.isEmpty()) return false
        val pts = if (!edge.points.isNullOrEmpty()) edge.points
        else listOf(GraphPoint(from.x, from.y), GraphPoint(to.x, to.y))
        for (region in blocked) {
            if (region.building != from.building || region.floor != from.floor) continue
            val owner = region.ownerId
            if (owner != null && (owner == from.id || owner == to.id)) continue
            if (crosses(pts, region.left, region.top, region.right, region.bottom)) return true
        }
        return false
    }

    private fun crosses(points: List<GraphPoint>, left: Double, top: Double, right: Double, bottom: Double): Boolean {
        for (i in 0 until points.size - 1) {
            val a = points[i]
            val b = points[i + 1]
            for (s in 1 until 100) {
                val x = a.x + (b.x - a.x) * s / 100
                val y = a.y + (b.y - a.y) * s / 100
                if (x > left && x < right && y > top && y < bottom) return true
            }
        }
        return false
    }

    private fun buildRoute(
        fromId: String,
        toId: String,
        byId: Map<String, Node>,
        cameFrom: Map<String, CameFrom>,
        seconds: Double
    ): Route {
        val legs = ArrayList<Leg>()
        var id = toId
        while (id != fromId) {
            val step = cameFrom[id]!!
            legs.add(toLeg(step.edge, byId[step.fromId]!!, byId[id]!!, step.reverse))
            id = step.fromId
        }
        legs.reverse()
        return Route(seconds.roundToInt(), legs)
    }

    private fun toLeg(edge: Edge, from: Node, to: Node, reverse: Boolean): Leg {
        val kind = travelKind(edge.kind, reverse)
        val points = pointsFor(edge, from, to, reverse)
        return if (kind == "walk") Leg(kind, from.building, from.floor, null, null, points)
        else Leg(kind, from.building, from.floor, to.building, to.floor, points)
    }

    private fun travelKind(kind: String, reverse: Boolean): String = if (!reverse) kind else when (kind) {
        "stair_up" -> "stair_down"
        "stair_down" -> "stair_up"
        else -> kind
    }

    private fun pointsFor(edge: Edge, from: Node, to: Node, reverse: Boolean): List<GraphPoint> {
        val poly = edge.points
        if (poly.isNullOrEmpty()) return listOf(GraphPoint(from.x, from.y), GraphPoint(to.x, to.y))
        return if (!reverse) poly else poly.asReversed()
    }

    private data class Link(val to: String, val edge: Edge, val reverse: Boolean)
    private data class CameFrom(val fromId: String, val edge: Edge, val reverse: Boolean)
    private data class Open(val id: String, val cost: SearchCost)

    private data class SearchCost(val seconds: Double, val stairs: Int) : Comparable<SearchCost> {
        override fun compareTo(other: SearchCost): Int {
            val bySeconds = seconds.compareTo(other.seconds)
            return if (bySeconds != 0) bySeconds else stairs.compareTo(other.stairs)
        }
    }
}
