package ru.bgtu_voenmeh.zapara.ui.maps

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.CancellationException
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

class MapsViewModel(
    private val container: AppContainer,
    private val roomArg: String?
) : ViewModel() {
    private val mutable = MutableStateFlow(MapsUiState())
    val state: StateFlow<MapsUiState> = mutable.asStateFlow()

    init {
        viewModelScope.launch {
            if (!roomArg.isNullOrBlank()) showRoom(roomArg) else toNext()
        }
        viewModelScope.launch { container.events.events.collect { refreshContext() } }
    }

    fun onEvent(event: MapsEvent) {
        when (event) {
            is MapsEvent.PickBuilding -> pickBuilding(event.index)
            is MapsEvent.PickFloor -> pickFloor(event.n)
            is MapsEvent.ShowRoom -> viewModelScope.launch { showRoom(event.classroomRaw) }
            MapsEvent.ToNext -> viewModelScope.launch { toNext() }
            MapsEvent.ZoomIn -> mutable.update { it.copy(zoom = (it.zoom * 1.25f).coerceIn(0.4f, 4f)) }
            MapsEvent.ZoomOut -> mutable.update { it.copy(zoom = (it.zoom / 1.25f).coerceIn(0.4f, 4f)) }
            MapsEvent.Fit -> mutable.update { it.copy(zoom = 1f) }
            is MapsEvent.Fullscreen -> mutable.update { it.copy(fullscreen = event.on) }
            is MapsEvent.Transform -> mutable.update { it.copy(zoom = event.zoom.coerceIn(0.4f, 4f)) }
        }
    }

    private suspend fun showRoom(classroomRaw: String) {
        val info = MapResolve.resolve(classroomRaw)
        if (info == null) { toNext(); return }
        if (info.isRemote) {
            mutable.update {
                it.copy(
                    loaded = true, remote = true, remoteNote = container.copy.get("maps_remote_line"),
                    highlight = null, planFile = null, contextLine = MapsComposer.contextLine(container.clock(), emptyList(), null, container.copy),
                    mode = MapMode.Lesson, note = info.note.ifBlank { null }
                )
            }
            return
        }
        val building = if (info.building == "ВЦ") "ГК" else info.building
        val file = withContext(Dispatchers.IO) { if (info.hasMap) container.mapStore.mapFile(info.fileName) else null }
        val coords = withContext(Dispatchers.IO) { container.mapStore.findCoords(building, info.floor, info.roomRaw) }
        val label = info.roomRaw.ifBlank { classroomRaw }
        mutable.update {
            it.copy(
                loaded = true, hasGroup = true, remote = false, building = building,
                floors = MapsComposer.floors(building), floor = info.floor, planFile = file,
                highlight = coords?.let { rect -> HighlightUi(rect, label) },
                mode = MapMode.Lesson, note = info.note.ifBlank { null },
                remoteNote = if (info.building == "ВЦ") container.copy.get("maps_vc_note") else null,
                contextLine = container.copy.get("maps_room_on_floor", label, info.floor, building)
            )
        }
    }

    private suspend fun toNext() {
        try {
            val now = container.clock()
            val prefs = withContext(Dispatchers.IO) { container.repo.settings() }
            val gid = prefs.myGroupId.orEmpty()
            val all = if (gid.isEmpty()) emptyList() else withContext(Dispatchers.IO) { container.repo.allForGroup(gid) }
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
                applyPlan("ГК", 1, MapMode.None, line, null)
                return
            }
            val info = MapResolve.resolve(target.classroomRaw)
            if (info?.isRemote == true) {
                mutable.update {
                    it.copy(loaded = true, hasGroup = gid.isNotEmpty(), remote = true, remoteNote = container.copy.get("maps_remote_line"),
                        contextLine = line, mode = MapMode.NextLesson, planFile = null, highlight = null)
                }
                return
            }
            val building = if (info?.building == "ВЦ") "ГК" else info?.building ?: "ГК"
            val floor = info?.floor ?: 1
            applyPlan(building, floor, MapMode.NextLesson, line, info?.classroomRaw, info?.roomRaw, info?.note, info?.building == "ВЦ")
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaMaps", "toNext", e)
            mutable.update { it.copy(loaded = true) }
        }
    }

    private suspend fun applyPlan(
        building: String, floor: Int, mode: MapMode, line: String, classroomRaw: String?,
        roomRaw: String? = null, note: String? = null, vc: Boolean = false
    ) {
        val fileName = MapResolve.MAP_FILES[building to floor]
        val file = withContext(Dispatchers.IO) { fileName?.let { container.mapStore.mapFile(it) } }
        val coords = withContext(Dispatchers.IO) {
            roomRaw?.let { container.mapStore.findCoords(building, floor, it) }
        }
        mutable.update {
            it.copy(
                loaded = true, hasGroup = true, remote = false, building = building,
                floors = MapsComposer.floors(building), floor = floor, planFile = file,
                highlight = coords?.let { rect -> HighlightUi(rect, roomRaw.orEmpty()) },
                contextLine = line, mode = mode, note = note?.ifBlank { null },
                remoteNote = if (vc) container.copy.get("maps_vc_note") else null
            )
        }
    }

    private fun pickBuilding(index: Int) {
        val building = mutable.value.buildings.getOrElse(index) { "ГК" }
        viewModelScope.launch { applyManual(building, MapsComposer.floors(building).first()) }
    }

    private fun pickFloor(n: Int) {
        viewModelScope.launch { applyManual(mutable.value.building, n) }
    }

    private suspend fun applyManual(building: String, floor: Int) {
        val fileName = MapResolve.MAP_FILES[building to floor]
        val file = withContext(Dispatchers.IO) { fileName?.let { container.mapStore.mapFile(it) } }
        mutable.update {
            it.copy(
                building = building, floors = MapsComposer.floors(building), floor = floor,
                planFile = file, highlight = null, mode = MapMode.Manual, remote = false,
                remoteNote = null, loaded = true
            )
        }
    }

    private suspend fun refreshContext() {
        if (mutable.value.mode == MapMode.Manual || mutable.value.mode == MapMode.Lesson) return
        toNext()
    }

    companion object {
        fun factory(container: AppContainer, roomArg: String?) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T = MapsViewModel(container, roomArg) as T
        }
    }
}
