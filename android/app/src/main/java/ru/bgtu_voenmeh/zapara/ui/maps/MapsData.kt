package ru.bgtu_voenmeh.zapara.ui.maps

import java.time.LocalDateTime
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.flow.Flow
import ru.bgtu_voenmeh.zapara.AppContainer
import ru.bgtu_voenmeh.zapara.data.CoordsRect
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.MapResolve
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.data.campus.CampusGraph
import ru.bgtu_voenmeh.zapara.ui.UiCopy
import ru.bgtu_voenmeh.zapara.ui.components.ToastKind

/** External data boundary only; routing, request ownership and publication remain in the VM. */
internal interface MapsData {
    val copy: UiCopy
    val events: Flow<*>
    fun clock(): LocalDateTime
    fun graph(): CampusGraph
    fun readLastEntrance(): String?
    fun rememberEntrance(graph: CampusGraph, id: String): String?
    fun settings(): ScheduleRepository.SettingsState
    fun allForGroup(id: String): List<Lesson>
    fun coords(): Map<String, Map<String, CoordsRect>>
    fun findCoords(building: String, floor: Int, room: String): CoordsRect?
    suspend fun catalog(ioDispatcher: CoroutineDispatcher): Map<FloorKey, FloorRaster>
    fun toast(message: String)
    fun loadError(error: Exception)
}

internal class ContainerMapsData(private val container: AppContainer) : MapsData {
    override val copy get() = container.copy
    override val events get() = container.events.events
    override fun clock() = container.clock()
    override fun graph() = container.mapStore.campusGraph()
    override fun readLastEntrance() = container.mapStore.readLastEntrance()
    override fun rememberEntrance(graph: CampusGraph, id: String) = container.mapStore.rememberEntrance(graph, id)
    override fun settings() = container.repo.settings()
    override fun allForGroup(id: String) = container.repo.allForGroup(id)
    override fun coords() = container.mapStore.coords()
    override fun findCoords(building: String, floor: Int, room: String) = container.mapStore.findCoords(building, floor, room)
    override suspend fun catalog(ioDispatcher: CoroutineDispatcher) =
        FloorRasterLoader(container.mapStore, ioDispatcher).load(MapResolve.MAP_FILES.keys.map { FloorKey(it.first, it.second) }.toSet())
    override fun toast(message: String) { container.toasts.show(message, ToastKind.Plain) }
    override fun loadError(error: Exception) { android.util.Log.w("ZaparaMaps", "toNext", error) }
}
