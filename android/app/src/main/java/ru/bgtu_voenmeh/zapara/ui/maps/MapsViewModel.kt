package ru.bgtu_voenmeh.zapara.ui.maps

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.AppContainer
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.MapResolve
import ru.bgtu_voenmeh.zapara.data.Schedule
import ru.bgtu_voenmeh.zapara.data.campus.CampusGraph
import ru.bgtu_voenmeh.zapara.data.campus.CampusRouter
import ru.bgtu_voenmeh.zapara.data.campus.Route

class MapsViewModel internal constructor(
    private val container: MapsData,
    private val roomArg: String?,
    private val routeDispatcher: CoroutineDispatcher = Dispatchers.Default,
    private val ioDispatcher: CoroutineDispatcher = Dispatchers.IO
) : ViewModel() {
    constructor(container: AppContainer, roomArg: String?, routeDispatcher: CoroutineDispatcher = Dispatchers.Default,
        ioDispatcher: CoroutineDispatcher = Dispatchers.IO) : this(ContainerMapsData(container), roomArg, routeDispatcher, ioDispatcher)

    private val mutable = MutableStateFlow(MapsUiState())
    val state: StateFlow<MapsUiState> = mutable.asStateFlow()
    private var graph: CampusGraph = CampusGraph.empty
    private var graphLoaded = false
    private var lastEntranceId: String? = null
    private var destRoomKey: String? = null
    private var prevRoomKey: String? = null
    private var fromId: String? = null
    private var toId: String? = null
    private var allPlaces: List<RoutePlaceUi> = emptyList()
    private var fallbackToastKey: String? = null
    private var floorRooms: List<FloorRoom> = emptyList()
    private val mapLoads = MapLoadRequests(viewModelScope)
    private val routeComputations = MapRouteComputations(routeDispatcher, ioDispatcher)
    private var routeResult: MapRouteResult? = null
    private var rasterRetry = 0L

    init {
        launchMap {
            ensureGraph()
            if (!roomArg.isNullOrBlank()) showRoom(roomArg) else toNext()
        }
        viewModelScope.launch { container.events.collect { refreshContext() } }
    }

    fun onEvent(event: MapsEvent) {
        when (event) {
            is MapsEvent.PickBuilding -> pickBuilding(event.index)
            is MapsEvent.PickFloor -> pickFloor(event.n)
            is MapsEvent.ShowRoom -> launchMap { showRoom(event.classroomRaw) }
            MapsEvent.ToNext -> launchMap { toNext() }
            MapsEvent.ZoomIn -> mutable.update { it.copy(zoom = (it.zoom * 1.25f).coerceIn(0.4f, 4f)) }
            MapsEvent.ZoomOut -> mutable.update { it.copy(zoom = (it.zoom / 1.25f).coerceIn(0.4f, 4f)) }
            MapsEvent.Fit -> mutable.update { it.copy(zoom = 1f, fitGeneration = it.fitGeneration + 1) }
            is MapsEvent.Fullscreen -> mutable.update { it.copy(fullscreen = event.on) }
            is MapsEvent.Transform -> mutable.update { it.copy(zoom = event.zoom.coerceIn(0.4f, 4f)) }
            is MapsEvent.PickEntrance -> launchMap { pickEntrance(event.id) }
            is MapsEvent.PickRouteStep -> mutable.value.presentation?.steps?.firstOrNull {
                it.from.building == event.building && it.from.floor == event.floor
            }?.let { selectStep(it.id) }
            is MapsEvent.SelectRouteStep -> selectStep(event.id)
            MapsEvent.PreviousRouteStep -> moveStep(-1)
            MapsEvent.NextRouteStep -> moveStep(1)
            MapsEvent.OpenRouteSteps -> mutable.update { it.copy(stepsOpen = true, picker = null, planPick = null) }
            MapsEvent.CloseRouteSteps -> mutable.update { it.copy(stepsOpen = false) }
            MapsEvent.RetryMaps -> launchMap { retryMaps() }
            is MapsEvent.MapDecodeFailed -> mutable.update { it.mapDecodeFailed(event.floor) }
            MapsEvent.ToggleStack -> mutable.update { it.copy(showStack = !it.showStack) }
            MapsEvent.OpenFrom -> openPicker(RouteField.From)
            MapsEvent.OpenTo -> openPicker(RouteField.To)
            is MapsEvent.QueryPlaces -> refreshPicker { it.copy(query = event.value) }
            is MapsEvent.PickPlace -> launchMap { pickPlace(event.id) }
            MapsEvent.ClosePicker -> mutable.update { it.copy(picker = null) }
            MapsEvent.SwapEnds -> launchMap { swapEnds() }
            is MapsEvent.FilterPickerBuilding -> refreshPicker { picker ->
                picker.copy(
                    building = event.building,
                    floors = event.building?.let { MapsComposer.floors(it) } ?: picker.floors
                )
            }
            is MapsEvent.FilterPickerFloor -> refreshPicker { it.copy(floor = event.floor) }
            is MapsEvent.PlanPress -> onPlanPress(event.nx, event.ny)
            is MapsEvent.PlanPickAs -> launchMap { pickPlanAs(event.field) }
            MapsEvent.ClosePlanPick -> mutable.update { it.copy(planPick = null) }
        }
    }

    private suspend fun ensureGraph(force: Boolean = false) {
        if (graphLoaded && !force) return
        val loaded = withContext(ioDispatcher) { container.graph() to container.readLastEntrance() }
        val places = withContext(routeDispatcher) { MapsComposer.places(loaded.first) }
        mapLoads.ensureCurrent()
        graph = loaded.first
        allPlaces = places
        if (!graphLoaded) lastEntranceId = loaded.second
        if (fromId == null) fromId = lastEntranceId
        graphLoaded = true
    }

    private suspend fun showRoom(classroomRaw: String) {
        ensureGraph()
        val info = MapResolve.resolve(classroomRaw)
        if (info == null) { toNext(); return }
        destRoomKey = info.classroomRaw
        toId = CampusRouter.resolveClassroom(graph, classroomRaw)?.id
        val initialStepId = computeRoute()
        val plan = MapsComposer.shownPlan(info.building, info.floor, info.roomRaw, CampusRouter.resolveClassroom(graph, classroomRaw))
        if (info.isRemote) {
            mapLoads.ensureCurrent()
            mutable.update {
                it.copy(
                    loaded = true, remote = true, remoteNote = container.copy.get("maps_remote_line"),
                    highlight = null, planFile = null, contextLine = MapsComposer.contextLine(container.clock(), emptyList(), null, container.copy),
                    mode = MapMode.Lesson, note = info.note.ifBlank { null }
                ).withRoute(info.building)
            }
            return
        }
        val building = plan.building
        val label = plan.roomRaw.ifBlank { classroomRaw }
        applyPlan(building, plan.floor, MapMode.Lesson,
            container.copy.get("maps_room_on_floor", label, plan.floor, building), plan.roomRaw,
            info.note, info.building == "ВЦ", followRoute = true, initialStepId = initialStepId)
    }

    private suspend fun toNext() {
        try {
            ensureGraph()
            val now = container.clock()
            val prefs = withContext(ioDispatcher) { container.settings() }
            val gid = prefs.myGroupId.orEmpty()
            val all = if (gid.isEmpty()) emptyList() else withContext(ioDispatcher) { container.allForGroup(gid) }
            val next = MapsComposer.nextLesson(now, { date ->
                Schedule.lessonsForDate(all, gid, date, prefs.periodStart, prefs.weekCount, prefs.parityInvert)
            })
            val todayLessons = if (gid.isEmpty()) emptyList() else
                Schedule.lessonsForDate(all, gid, now.toLocalDate(), prefs.periodStart, prefs.weekCount, prefs.parityInvert)
            val going = todayLessons.firstOrNull { l ->
                val start = runCatching { java.time.LocalTime.parse(l.timeStart) }.getOrNull()
                val end = runCatching { java.time.LocalTime.parse(l.timeEnd) }.getOrNull()
                val t = now.toLocalTime()
                start != null && end != null && !t.isBefore(start) && t.isBefore(end)
            }
            val target: Lesson? = going ?: next?.second
            val line = MapsComposer.contextLine(now, todayLessons, next, container.copy)
            if (target == null) {
                destRoomKey = null
                prevRoomKey = null
                toId = null
                computeRoute()
                applyPlan("ГК", 1, MapMode.None, line, null)
                return
            }
            destRoomKey = target.classroomRaw
            val targetDate = going?.let { now.toLocalDate() } ?: next?.first ?: now.toLocalDate()
            prevRoomKey = MapsComposer.previousLessonToday(todayLessons, target, now, targetDate)?.classroomRaw
            toId = CampusRouter.resolveClassroom(graph, target.classroomRaw)?.id
            fromId = CampusRouter.resolveClassroom(graph, prevRoomKey)?.id ?: lastEntranceId
            val initialStepId = computeRoute()
            val info = MapResolve.resolve(target.classroomRaw)
            if (info?.isRemote == true) {
                mapLoads.ensureCurrent()
                mutable.update {
                    it.copy(loaded = true, hasGroup = gid.isNotEmpty(), remote = true, remoteNote = container.copy.get("maps_remote_line"),
                        contextLine = line, mode = MapMode.NextLesson, planFile = null, highlight = null).withRoute(info.building)
                }
                return
            }
            val plan = if (info == null) ShownPlan("ГК", 1, "")
            else MapsComposer.shownPlan(info.building, info.floor, info.roomRaw, CampusRouter.resolveClassroom(graph, target.classroomRaw))
            applyPlan(plan.building, plan.floor, MapMode.NextLesson, line, plan.roomRaw, info?.note, info?.building == "ВЦ",
                followRoute = true, initialStepId = initialStepId)
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            mapLoads.ensureCurrent()
            container.loadError(e)
            mutable.update { it.copy(loaded = true).withRoute(it.building)
                .copy(rasterCatalog = it.rasterCatalog, activeStepId = it.activeStepId,
                    routeFailure = container.copy.get("maps_route_load_failed")) }
        }
    }

    private suspend fun applyPlan(
        building: String, floor: Int, mode: MapMode, line: String,
        roomRaw: String? = null, note: String? = null, vc: Boolean = false,
        followRoute: Boolean = false, selectionId: Int? = null, initialStepId: Int? = null
    ) {
        val base = mutable.value.withRoute(building).copy(activeStepId = mutable.value.activeStepId)
        val selectedId = selectionId ?: if (followRoute) initialStepId ?: base.activeStepId else null
        val selected = selectedId?.let { RouteNavigation.select(base, it) }
        val shown = selected?.building ?: shownBuilding(building)
        val level = selected?.floor ?: floor
        val room = if (selected != null) MapsComposer.highlightRoom(shown, level, node(toId), node(fromId)) else roomRaw
        val coords = withContext(ioDispatcher) {
            room?.let { container.findCoords(shown, level, it) }
        }
        val catalog = loadCatalog()
        val rasters = floorFilesFor(shown, catalog)
        val rooms = loadFloorRooms(shown, level)
        mapLoads.ensureCurrent()
        floorRooms = rooms
        mutable.update {
            val routed = it.withRoute(shown).copy(activeStepId = it.activeStepId)
            val chosen = selectedId?.let { id -> RouteNavigation.select(routed, id) } ?: routed
            rasters.applyTo(chosen).copy(
                loaded = true, hasGroup = true, remote = false,
                rasterCatalog = catalog,
                floor = level, planFile = rasters.files[level],
                highlight = coords?.let { rect -> HighlightUi(rect, room.orEmpty()) },
                roomUnmarked = !room.isNullOrBlank() && coords == null,
                contextLine = line, mode = mode, note = note?.ifBlank { null },
                remoteNote = if (vc || MapResolve.resolve(destRoomKey)?.building == "ВЦ") container.copy.get("maps_vc_note") else null
            ).withRoute(shown).copy(rasterCatalog = catalog)
        }
    }

    private fun pickBuilding(index: Int) {
        val building = mutable.value.buildings.getOrElse(index) { "ГК" }
        launchMap { applyManual(building, MapsComposer.floors(building).first()) }
    }

    private fun pickFloor(n: Int, building: String? = null) {
        launchMap { applyManual(building ?: mutable.value.building, n) }
    }

    private suspend fun applyManual(building: String, floor: Int) {
        ensureGraph()
        val room = MapsComposer.highlightRoom(building, floor, node(toId), node(fromId))
        applyPlan(building, floor, MapMode.Manual, mutable.value.contextLine, room)
    }

    private suspend fun pickEntrance(id: String) {
        ensureGraph()
        val graphSnapshot = graph
        val remembered = withContext(ioDispatcher) { container.rememberEntrance(graphSnapshot, id) }
        if (remembered == null) return
        mapLoads.ensureCurrent()
        lastEntranceId = remembered
        fromId = remembered
        prevRoomKey = null
        val start = node(fromId)
        if (start != null) revealNode(start) else mutable.update { it.withRoute(it.building) }
    }

    private fun openPicker(field: RouteField) {
        val state = mutable.value
        val building = state.building
        val floor = state.floor
        mutable.update {
            it.copy(
                stepsOpen = false, planPick = null,
                picker = RoutePickerUi(
                    field, "",
                    MapsComposer.pickerItems(allPlaces, "", building, floor),
                    building, floor, state.buildings, MapsComposer.floors(building)
                )
            )
        }
    }

    private fun refreshPicker(update: (RoutePickerUi) -> RoutePickerUi) {
        val picker = mutable.value.picker ?: return
        val next = update(picker)
        mutable.update {
            it.copy(
                picker = next.copy(
                    items = MapsComposer.pickerItems(allPlaces, next.query, next.building, next.floor)
                )
            )
        }
    }

    private fun onPlanPress(nx: Double, ny: Double) {
        val hit = MapsComposer.hitRoom(nx, ny, floorRooms) ?: return
        mutable.update { it.copy(planPick = PlanPickUi(hit.id, hit.room), stepsOpen = false, picker = null) }
    }

    private suspend fun pickPlanAs(field: RouteField) {
        val pick = mutable.value.planPick ?: return
        mutable.update { it.copy(planPick = null, picker = null) }
        applyPlace(pick.id, field)
    }

    private suspend fun pickPlace(id: String) {
        val field = mutable.value.picker?.field ?: return
        mutable.update { it.copy(picker = null) }
        applyPlace(id, field)
    }

    private suspend fun applyPlace(id: String, field: RouteField) {
        ensureGraph()
        val chosen = node(id) ?: return
        if (field == RouteField.From) {
            val graphSnapshot = graph
            mapLoads.selectFromPlace(MapFromSelection(fromId, lastEntranceId, prevRoomKey), chosen,
                rememberEntrance = { entrance ->
                    withContext(ioDispatcher) { container.rememberEntrance(graphSnapshot, entrance) }
                },
                commit = { selected ->
                    fromId = selected.fromId
                    lastEntranceId = selected.lastEntranceId
                    prevRoomKey = selected.prevRoomKey
                })
        } else {
            toId = chosen.id
            destRoomKey = chosen.room ?: chosen.id
        }
        revealNode(chosen)
    }

    private suspend fun swapEnds() {
        if (fromId == null || toId == null) return
        val oldFrom = fromId
        fromId = toId
        toId = oldFrom
        val oldPrev = prevRoomKey
        prevRoomKey = destRoomKey
        destRoomKey = oldPrev
        val focus = node(fromId) ?: node(toId) ?: return
        revealNode(focus)
    }

    private fun node(id: String?) = id?.let { key -> graph.nodes.firstOrNull { it.id == key } }

    private suspend fun revealNode(focus: ru.bgtu_voenmeh.zapara.data.campus.Node) {
        val initialStepId = computeRoute()
        val dest = node(toId)
        val shown = if (focus.building == "ВЦ") "ГК" else focus.building
        val highlightRoom = dest?.takeIf { it.building == focus.building && it.floor == focus.floor }?.room
            ?: focus.room
        applyPlan(shown, focus.floor, MapMode.Manual, mutable.value.contextLine, highlightRoom,
            followRoute = true, initialStepId = initialStepId)
    }

    private suspend fun loadFloorRooms(building: String, floor: Int): List<FloorRoom> {
        ensureGraph()
        val shown = if (building == "ВЦ") "ГК" else building
        val map = withContext(ioDispatcher) { container.coords()["$shown $floor"].orEmpty() }
        return MapsComposer.floorRooms(graph, shown, floor, map)
    }

    private suspend fun loadCatalog(): Map<FloorKey, FloorRaster> =
        container.catalog(ioDispatcher).filterKeys { it !in mutable.value.decodeFailedFloors }

    private suspend fun floorFilesFor(building: String, catalog: Map<FloorKey, FloorRaster>): MapRasterPublication =
        MapRasterPublication.capture(building, ioDispatcher, rasterRetry) { shown ->
            catalog.filterKeys { it.building == shown }.mapKeys { it.key.floor }.mapValues { it.value.file }
        }

    private suspend fun computeRoute(retryStepId: Int? = null, retryRoute: Route? = null): Int? {
        val input = MapRouteInput.capture(graph, fromId, toId, prevRoomKey, destRoomKey, lastEntranceId)
        routeResult = null
        // Endpoint commit and hiding mismatched geometry have no cancellable boundary between them.
        mutable.update { MapRouteState.begin(it.withRoute(it.building),
            node(fromId)?.let(MapsComposer::placeLabel).orEmpty(), node(toId)?.let(MapsComposer::placeLabel).orEmpty()) }
        if (MapResolve.resolve(destRoomKey)?.isRemote == true) {
            mutable.update { it.copy(routeLoading = false, showStack = false, stepsOpen = false) }
            return null
        }
        val result = try {
            routeComputations.compute(input) { loadCatalog() }
        } catch (e: CancellationException) { throw e }
        catch (_: Exception) {
            mapLoads.ensureCurrent()
            mutable.update { it.copy(routeLoading = false, routeFailure = container.copy.get("maps_route_load_failed")) }
            return null
        }
        mapLoads.ensureCurrent()
        routeResult = result
        val from = result.from
        val guessed = result.guessed
        if (from != null) fromId = from.id
        if (result.to != null) toId = result.to.id
        val initialStepId = result.presentation?.steps?.firstOrNull { it.id == retryStepId && result.route == retryRoute }?.id
            ?: result.presentation?.steps?.firstOrNull()?.id
        if (guessed != null && from != null && guessed.id != from.id) {
            val key = "${guessed.id}->${from.id}"
            if (fallbackToastKey != key) {
                fallbackToastKey = key
                MapsComposer.startFallbackMessage(MapsComposer.placeLabel(from), container.copy)?.let {
                    container.toast(it)
                }
            }
        } else {
            fallbackToastKey = null
        }
        return initialStepId
    }

    private fun MapsUiState.withRoute(building: String): MapsUiState = MapRouteState.decorate(
        this, building, graph, fromId, toId, lastEntranceId, routeResult, null, container.copy)

    private fun selectStep(id: Int) {
        val current = mutable.value
        val selected = RouteNavigation.select(current, id)
        if (selected === current) return
        launchMap {
            applyPlan(selected.building, selected.floor, MapMode.Manual, mutable.value.contextLine, selectionId = id)
        }
    }

    private fun moveStep(delta: Int) {
        val current = mutable.value
        val next = RouteNavigation.move(current, delta)
        if (next !== current) next.activeStepId?.let { selectStep(it) }
    }

    private suspend fun retryMaps() {
        val selected = mutable.value.activeStepId
        val previousRoute = mutable.value.route
        rasterRetry++
        mutable.update { it.copy(decodeFailedFloors = emptySet()) }
        ensureGraph(force = true)
        val initialStepId = computeRoute(selected, previousRoute)
        val current = mutable.value
        if (MapResolve.resolve(destRoomKey)?.isRemote == true) {
            mutable.update { it.withRoute(it.building).copy(remote = true, planFile = null,
                highlight = null, remoteNote = container.copy.get("maps_remote_line")) }
            return
        }
        applyPlan(current.building, current.floor, current.mode, current.contextLine,
            followRoute = true, initialStepId = initialStepId)
    }

    private fun launchMap(block: suspend () -> Unit) = mapLoads.launch {
        try { block() }
        catch (e: CancellationException) { throw e }
        catch (_: Exception) {
            mapLoads.ensureCurrent()
            mutable.update { it.copy(routeLoading = false, routeFailure = container.copy.get("maps_route_load_failed")) }
        }
    }

    private suspend fun refreshContext() {
        mapLoads.refresh(mutable.value.mode) { toNext() }
    }

    companion object {
        fun factory(container: AppContainer, roomArg: String?) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T = MapsViewModel(container, roomArg) as T
        }
    }
}
