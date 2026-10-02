package ru.bgtu_voenmeh.zapara.ui.maps

import java.io.File
import ru.bgtu_voenmeh.zapara.data.campus.Route

data class EntranceUi(val id: String, val label: String, val selected: Boolean)

data class RouteStepUi(val text: String, val building: String, val floor: Int)

data class StairMarkerUi(val x: Double, val y: Double, val label: String, val isDeparture: Boolean)

enum class RouteField { From, To }

data class FloorRoom(val id: String, val room: String, val rect: ru.bgtu_voenmeh.zapara.data.CoordsRect)

data class PlanPickUi(val id: String, val label: String)

data class RoutePlaceUi(
    val id: String,
    val label: String,
    val hint: String,
    val kind: String,
    val building: String,
    val floor: Int,
    val hay: String
)

data class RoutePickerUi(
    val field: RouteField,
    val query: String,
    val items: List<RoutePlaceUi>,
    val building: String? = null,
    val floor: Int? = null,
    val buildings: List<String> = listOf("ГК", "УЛК"),
    val floors: List<Int> = emptyList(),
    val epoch: Long = 0,
    val error: String? = null,
    val searchCampus: Boolean = true,
    val recent: List<RoutePlaceUi> = emptyList(),
    val selectedFrom: Boolean = false,
    val selectedTo: Boolean = false,
    val pinned: List<RoutePlaceUi> = emptyList()
)

data class MapsUiState(
    val loaded: Boolean = false,
    val hasGroup: Boolean = false,
    val buildings: List<String> = listOf("ГК", "УЛК"),
    val building: String = "ГК",
    val floors: List<Int> = (1..4).toList(),
    val floor: Int = 1,
    val planFile: File? = null,
    val highlight: HighlightUi? = null,
    val contextLine: String = "",
    val mode: MapMode = MapMode.None,
    val remoteNote: String? = null,
    val note: String? = null,
    val fullscreen: Boolean = false,
    val zoom: Float = 1f,
    val fitGeneration: Int = 0,
    val remote: Boolean = false,
    val unmarked: Boolean = true,
    val routeUnmarked: String = "",
    val path: List<List<Pair<Float, Float>>> = emptyList(),
    val stairMarkers: List<StairMarkerUi> = emptyList(),
    val routeSteps: List<RouteStepUi> = emptyList(),
    val entrances: List<EntranceUi> = emptyList(),
    val showStack: Boolean = false,
    val route: Route? = null,
    val floorFiles: Map<Int, File> = emptyMap(),
    val fromLabel: String = "",
    val toLabel: String = "",
    val durationLabel: String = "",
    val canSwap: Boolean = false,
    val picker: RoutePickerUi? = null,
    val planPick: PlanPickUi? = null,
    val stackRasterRevision: StackRasterRevision? = null,
    val presentation: RoutePresentation? = null,
    val activeStepId: Int? = null,
    val stepsOpen: Boolean = false,
    val routeLoading: Boolean = false,
    val routeFailure: String? = null,
    val rasterCatalog: Map<FloorKey, FloorRaster> = emptyMap(),
    val decodeFailedFloors: Set<FloorKey> = emptySet(),
    val roomUnmarked: Boolean = false,
    val availableRooms: List<FloorRoom> = emptyList(),
    val alphaMaps: Boolean = false,
    val mapError: String? = null,
    val automaticNote: String? = null,
    val canUndoEndpoint: Boolean = false,
    val recentRoutes: List<RecentRouteUi> = emptyList()
)

internal fun RoutePickerUi.withBuilding(next: String?): RoutePickerUi {
    val available = if (next == null) buildings.flatMap(MapsComposer::floors).distinct().sorted()
        else MapsComposer.floors(next)
    return copy(building = next, floors = available, floor = floor?.takeIf { it in available })
}

internal fun RoutePickerUi.clearFilters(): RoutePickerUi = copy(
    query = "", building = null, floor = null, searchCampus = true,
    floors = buildings.flatMap(MapsComposer::floors).distinct().sorted(), error = null)

internal fun RoutePickerUi.accepts(pick: MapsEvent.PickPlace): Boolean =
    epoch == pick.epoch && field == pick.field &&
        (items.any { it.id == pick.id } || query.isBlank() &&
            (recent.any { it.id == pick.id } || pinned.any { it.id == pick.id }))

internal fun RoutePickerUi.switchTo(field: RouteField, epoch: Long): RoutePickerUi =
    copy(field = field, epoch = epoch, error = null)

internal class RecentPlaceIds(private val limit: Int = 5) {
    private val ids = ArrayDeque<String>()
    fun remember(id: String) {
        ids.remove(id)
        ids.addFirst(id)
        while (ids.size > limit) ids.removeLast()
    }
    fun clear() = ids.clear()
    fun items(): List<String> = ids.toList()
}

/** Return to a local manual map after both route endpoints have been cleared. */
internal fun MapsUiState.clearedRouteDisplay(): MapsUiState = copy(
    mode = MapMode.Manual, contextLine = "", note = null, remoteNote = null, remote = false,
    automaticNote = null, highlight = null, roomUnmarked = false,
    route = null, presentation = null, path = emptyList(), stairMarkers = emptyList(),
    routeSteps = emptyList(), activeStepId = null, routeLoading = false,
    routeFailure = null, routeUnmarked = "", unmarked = true,
    showStack = false, stepsOpen = false, picker = null, planPick = null,
    fromLabel = "", toLabel = "", durationLabel = "", canSwap = false
)

fun MapsUiState.withoutRouting(): MapsUiState = copy(
    alphaMaps = false,
    showStack = false,
    stepsOpen = false,
    picker = null,
    planPick = null,
    route = null,
    presentation = null,
    path = emptyList(),
    stairMarkers = emptyList(),
    routeSteps = emptyList(),
    fromLabel = "",
    toLabel = "",
    durationLabel = "",
    canSwap = false,
    activeStepId = null,
    routeLoading = false,
    routeFailure = null
)

sealed interface MapsEvent {
    data class PickBuilding(val index: Int) : MapsEvent
    data class PickFloor(val n: Int) : MapsEvent
    data class ShowRoom(val classroomRaw: String) : MapsEvent
    data class FocusRoom(val id: String) : MapsEvent
    data object Browse : MapsEvent
    data object ToNext : MapsEvent
    data object ZoomIn : MapsEvent
    data object ZoomOut : MapsEvent
    data object Fit : MapsEvent
    data class Fullscreen(val on: Boolean) : MapsEvent
    data class Transform(val zoom: Float) : MapsEvent
    data class PickEntrance(val id: String) : MapsEvent
    data class PickRouteStep(val building: String, val floor: Int) : MapsEvent
    data class SelectRouteStep(val id: Int) : MapsEvent
    data object PreviousRouteStep : MapsEvent
    data object NextRouteStep : MapsEvent
    data object OpenRouteSteps : MapsEvent
    data object CloseRouteSteps : MapsEvent
    data object RetryMaps : MapsEvent
    data class MapDecodeFailed(val floor: FloorKey) : MapsEvent
    data object ToggleStack : MapsEvent
    data object OpenFrom : MapsEvent
    data object OpenTo : MapsEvent
    data class QueryPlaces(val value: String) : MapsEvent
    data class PickPlace(val id: String, val field: RouteField, val epoch: Long) : MapsEvent
    data object ClosePicker : MapsEvent
    data class SwitchPickerField(val field: RouteField) : MapsEvent
    data object ClearRecentPlaces : MapsEvent
    data object PickerCurrentFloor : MapsEvent
    data class RevealEndpoint(val field: RouteField) : MapsEvent
    data object SwapEnds : MapsEvent
    data class FilterPickerBuilding(val building: String?) : MapsEvent
    data class FilterPickerFloor(val floor: Int?) : MapsEvent
    data object ResetPickerFilters : MapsEvent
    data class SearchCampus(val enabled: Boolean) : MapsEvent
    data class ClearEndpoint(val field: RouteField) : MapsEvent
    data object UndoEndpoint : MapsEvent
    data class RepeatRoute(val fromId: String, val toId: String) : MapsEvent
    data object ClearRecentRoutes : MapsEvent
    data class TogglePinnedPlace(val id: String, val epoch: Long) : MapsEvent
    data class PlanPress(val nx: Double, val ny: Double) : MapsEvent
    data class PlanPickAs(val field: RouteField) : MapsEvent
    data object ClosePlanPick : MapsEvent
}

/** A decode notification is not a retry and never replaces the accepted route model. */
internal fun MapsUiState.mapDecodeFailed(key: FloorKey): MapsUiState {
    if (remote || key != FloorKey(building, floor) || key in decodeFailedFloors) return this
    return copy(planFile = null, highlight = null, rasterCatalog = rasterCatalog - key,
        floorFiles = floorFiles - key.floor, decodeFailedFloors = decodeFailedFloors + key,
        availableRooms = emptyList())
}
