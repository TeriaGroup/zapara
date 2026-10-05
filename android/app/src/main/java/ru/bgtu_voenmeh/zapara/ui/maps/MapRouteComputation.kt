package ru.bgtu_voenmeh.zapara.ui.maps

import java.util.Collections
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.Job
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.data.campus.CampusGraph
import ru.bgtu_voenmeh.zapara.data.campus.CampusRouter
import ru.bgtu_voenmeh.zapara.data.campus.Node
import ru.bgtu_voenmeh.zapara.data.campus.Route

internal class MapRouteInput private constructor(
    val graph: CampusGraph, val fromId: String?, val toId: String?,
    val previousRoom: String?, val destinationRoom: String?, val entranceId: String?
) {
    companion object {
        fun capture(graph: CampusGraph, fromId: String?, toId: String?, previousRoom: String?, destinationRoom: String?, entranceId: String?) =
            MapRouteInput(graph.copy(buildings = graph.buildings.toList(), nodes = graph.nodes.toList(),
                edges = graph.edges.map { it.copy(points = it.points?.toList()) }, blocked = graph.blocked.toList()),
                fromId, toId, previousRoom, destinationRoom, entranceId)
    }
}

internal data class MapRouteResult(
    val from: Node?, val to: Node?, val guessed: Node?, val route: Route?,
    val presentation: RoutePresentation?, val catalog: Map<FloorKey, FloorRaster>, val failure: String?
)

/** Main-confined ownership; the CPU worker only reads its captured input and catalog. */
internal class MapRouteComputations(private val routeDispatcher: CoroutineDispatcher, private val ioDispatcher: CoroutineDispatcher) {
    private var generation = 0L
    private var job: Job? = null

    suspend fun compute(input: MapRouteInput, loadCatalog: suspend () -> Map<FloorKey, FloorRaster>): MapRouteResult = coroutineScope {
        val request = ++generation
        job?.cancel()
        job = currentCoroutineContext()[Job]
        val catalog = withContext(ioDispatcher) { Collections.unmodifiableMap(LinkedHashMap(loadCatalog())) }
        val result = withContext(routeDispatcher) { build(input, catalog) }
        currentCoroutineContext().ensureActive()
        if (generation != request) throw CancellationException("Superseded route computation")
        result
    }

    private fun build(input: MapRouteInput, catalog: Map<FloorKey, FloorRaster>): MapRouteResult {
        val graph = input.graph
        fun node(id: String?) = graph.nodes.firstOrNull { it.id == id }
        val dest = if (input.toId != null) node(input.toId) else CampusRouter.resolveClassroom(graph, input.destinationRoom)
        val guessed = if (input.fromId != null) node(input.fromId) else
            CampusRouter.resolveClassroom(graph, input.previousRoom) ?: CampusRouter.resolveEntrance(graph, input.entranceId)
        val from = MapsComposer.startFor(graph, guessed?.id, dest?.id) ?: guessed
        val result = if (from != null && dest != null) CampusRouter.find(graph, from.id, dest.id) else null
        val route = result?.route
        val failure = when {
            graph.nodes.isEmpty() -> "maps_route_graph_missing"
            dest == null && input.toId == null && input.destinationRoom == null -> null
            from == null || dest == null -> "maps_route_unknown"
            result?.ok != true -> "maps_route_unreachable"
            else -> null
        }
        return MapRouteResult(from, dest, guessed, route,
            route?.let { RoutePresentationBuilder.build(it, from, dest, catalog.mapValues { entry -> entry.value.size }) }, catalog, failure)
    }
}
