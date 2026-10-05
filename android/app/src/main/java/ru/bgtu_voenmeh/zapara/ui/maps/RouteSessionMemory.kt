package ru.bgtu_voenmeh.zapara.ui.maps

internal data class RoutePair(val from: String, val to: String)
data class RecentRouteUi(val fromId: String, val toId: String, val label: String)

/** Explicit, bounded memory; never shared with another profile/group or persisted remotely. */
internal class RouteSessionMemory {
    private var scope: String? = null
    private val routes = ArrayDeque<RoutePair>()
    private val pins = LinkedHashSet<String>()
    var undo: Pair<RouteField, String>? = null
        private set

    fun enter(next: String): Boolean {
        if (scope == next) return false
        scope = next
        routes.clear(); pins.clear(); undo = null
        return true
    }
    fun remember(from: String, to: String) {
        if (from == to) return
        val pair = RoutePair(from, to)
        routes.remove(pair); routes.addFirst(pair)
        while (routes.size > 6) routes.removeLast()
    }
    fun history() = routes.toList()
    fun clearRoutes() = routes.clear()
    fun pinned() = pins.toList()
    fun togglePin(id: String) {
        if (!pins.remove(id) && pins.size < 8) pins.add(id)
    }
    fun prune(valid: Set<String>) {
        pins.retainAll(valid)
        routes.removeAll { it.from !in valid || it.to !in valid }
        if (undo?.second !in valid) undo = null
    }
    fun cleared(field: RouteField, id: String?) { undo = id?.let { field to it } }
    fun changed() { undo = null }
}
