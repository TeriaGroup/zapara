package ru.bgtu_voenmeh.zapara.ui.maps

import ru.bgtu_voenmeh.zapara.data.campus.CampusGraph
import ru.bgtu_voenmeh.zapara.data.campus.CampusRouter
import ru.bgtu_voenmeh.zapara.ui.UiCopy

/** Pure state projection shared by route requests and manual map publication. */
internal object MapRouteState {
    fun begin(state: MapsUiState, fromLabel: String, toLabel: String): MapsUiState = state.copy(
        route = null, presentation = null, activeStepId = null, routeLoading = true, routeFailure = null,
        path = emptyList(), stairMarkers = emptyList(), routeSteps = emptyList(), highlight = null,
        durationLabel = "", fromLabel = fromLabel, toLabel = toLabel, unmarked = true,
        stepsOpen = false, showStack = false
    )

    fun decorate(state: MapsUiState, building: String, graph: CampusGraph, fromId: String?, toId: String?,
        entranceId: String?, result: MapRouteResult?, selectedId: Int?, copy: UiCopy): MapsUiState {
        val shown = shownBuilding(building)
        val computed = result?.route
        val from = graph.nodes.firstOrNull { it.id == fromId }
        val to = graph.nodes.firstOrNull { it.id == toId }
        return state.copy(
            presentation = result?.presentation, activeStepId = selectedId ?: state.activeStepId,
            routeLoading = false,
            routeFailure = if (result != null) result.failure?.let { copy.get(it) } else state.routeFailure,
            rasterCatalog = result?.catalog ?: state.rasterCatalog, unmarked = computed == null,
            routeUnmarked = result?.failure?.let { copy.get(it) } ?: when {
                from == null || to == null -> copy.get("maps_route_need")
                else -> copy.get("route_unmarked")
            },
            path = MapsComposer.floorPathStrokes(computed, shown, state.floor),
            stairMarkers = MapsComposer.stairMarkers(computed, shown, state.floor),
            routeSteps = computed?.let { MapsComposer.formatRouteSteps(it, copy) } ?: emptyList(), route = computed,
            entrances = CampusRouter.entrances(graph).map { EntranceUi(it.id, it.label?.ifBlank { null } ?: it.id, it.id == entranceId) },
            fromLabel = from?.let { MapsComposer.placeLabel(it) }.orEmpty(),
            toLabel = to?.let { MapsComposer.placeLabel(it) }.orEmpty(),
            durationLabel = computed?.let { MapsComposer.durationLabel(it.seconds.toDouble(), copy) }.orEmpty(),
            canSwap = from != null && to != null && from.id != to.id
        )
    }
}
