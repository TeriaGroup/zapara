package ru.bgtu_voenmeh.zapara.ui.maps

import java.io.IOException
import java.time.LocalDateTime
import kotlin.coroutines.CoroutineContext
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.*
import kotlinx.coroutines.withContext
import androidx.lifecycle.viewModelScope
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import ru.bgtu_voenmeh.zapara.data.*
import ru.bgtu_voenmeh.zapara.data.campus.*
import ru.bgtu_voenmeh.zapara.ui.UiCopy

@OptIn(ExperimentalCoroutinesApi::class)
class MapsViewModelRecoveryTest {
    @get:Rule val files = TemporaryFolder()

    private class Gate(private val delegate: CoroutineDispatcher) : CoroutineDispatcher() {
        var hold = false
        var calls = 0
        private val queue = ArrayDeque<Pair<CoroutineContext, Runnable>>()
        val pending get() = queue.size
        override fun dispatch(context: CoroutineContext, block: Runnable) {
            calls++
            if (hold) queue.addLast(context to block) else delegate.dispatch(context, block)
        }
        fun releaseAll() {
            hold = false
            while (queue.isNotEmpty()) queue.removeFirst().let { delegate.dispatch(it.first, it.second) }
        }
    }

    private inner class Data : MapsData {
        override val copy = UiCopy { key, _ -> key }
        override val events = MutableSharedFlow<Unit>()
        val entrance = Node("ulk.entrance.main", "entrance", "УЛК", 1, .1, .1, label = "Вход")
        val stairs1 = Node("s1", "stairs", "УЛК", 1, .3, .1)
        val stairs5 = Node("s5", "stairs", "УЛК", 5, .3, .1)
        val room = Node("ulk.room.564", "room", "УЛК", 5, .8, .1, room = "564")
        private val graph = CampusGraph(1, listOf("УЛК"), listOf(entrance, stairs1, stairs5, room), listOf(
            Edge(entrance.id, stairs1.id, "walk", 10.0, false),
            Edge(stairs1.id, stairs5.id, "stair_up", 20.0, false),
            Edge(stairs5.id, room.id, "walk", 10.0, false)))
        val rect = CoordsRect(.7, .1, .1, .1)
        val rasters = listOf(1, 5).associate { floor ->
            FloorKey("УЛК", floor) to FloorRaster(files.newFile(MapResolve.MAP_FILES.getValue("УЛК" to floor)), RasterSize(100, 100))
        }
        var catalogCalls = 0
        var barrierCall = -1
        var entered = CompletableDeferred<Unit>()
        var release = CompletableDeferred<Unit>()
        var failPublication = false
        var finishCancelledIo = false
        val errors = mutableListOf<Exception>()
        override fun clock() = LocalDateTime.of(2026, 9, 14, 8, 0)
        override fun graph() = graph
        override fun readLastEntrance() = entrance.id
        override fun rememberEntrance(graph: CampusGraph, id: String) = id
        override fun settings() = ScheduleRepository.SettingsState(myGroupId = "test", mapsAlpha = true)
        override fun allForGroup(id: String) = listOf(Lesson(groupId = id, dayOfWeek = 1,
            timeStart = "09:00", timeEnd = "10:30", classroomRaw = "564*"))
        override fun coords() = mapOf("УЛК 5" to mapOf("564" to rect))
        override fun findCoords(building: String, floor: Int, room: String) = coords()["$building $floor"]?.get(room)
        override suspend fun catalog(ioDispatcher: CoroutineDispatcher): Map<FloorKey, FloorRaster> = withContext(ioDispatcher) {
            if (++catalogCalls == barrierCall) {
                entered.complete(Unit)
                if (finishCancelledIo) withContext(NonCancellable) { release.await() } else release.await()
                if (failPublication) throw IOException("final map publication")
            }
            rasters.toMap()
        }
        override fun toast(message: String) = Unit
        override fun loadError(error: Exception) { errors += error }
    }

    private fun fixture(block: suspend TestScope.(MapsViewModel, Data, Gate) -> Unit) = runTest {
        val dispatcher = StandardTestDispatcher(testScheduler)
        Dispatchers.setMain(dispatcher)
        val data = Data()
        val cpu = Gate(dispatcher)
        val vm = MapsViewModel(data, "564*", cpu, dispatcher)
        try {
            runCurrent()
            assertNotNull(vm.state.value.route)
            assertEquals(3, vm.state.value.presentation!!.steps.size)
            block(vm, data, cpu)
        } finally {
            vm.viewModelScope.cancel()
            data.release.complete(Unit)
            cpu.releaseAll()
            runCurrent()
            Dispatchers.resetMain()
        }
    }

    @Test fun decode_failure_waits_for_manual_retry_and_ignores_other_floor() = fixture { vm, data, _ ->
        val before = vm.state.value
        val calls = data.catalogCalls
        vm.onEvent(MapsEvent.MapDecodeFailed(FloorKey("ГК", 1)))
        assertSame(before, vm.state.value)
        vm.onEvent(MapsEvent.MapDecodeFailed(FloorKey("УЛК", 1)))
        runCurrent()
        assertNull(vm.state.value.planFile)
        assertSame(before.route, vm.state.value.route)
        assertSame(before.presentation, vm.state.value.presentation)
        assertEquals(calls, data.catalogCalls)
        vm.onEvent(MapsEvent.SelectRouteStep(before.activeStepId!!))
        runCurrent()
        assertNull(vm.state.value.planFile)
        vm.onEvent(MapsEvent.NextRouteStep)
        runCurrent()
        assertSame(before.route, vm.state.value.route)
        vm.onEvent(MapsEvent.RetryMaps)
        runCurrent()
        assertTrue(vm.state.value.decodeFailedFloors.isEmpty())
        assertNotNull(vm.state.value.planFile)
        assertEquals(1, vm.state.value.activeStepId)
    }

    @Test fun retry_interrupted_remote_to_physical_publishes_physical_bundle_and_stays_coherent() = fixture { vm, data, cpu ->
        vm.onEvent(MapsEvent.ShowRoom("дистанционно"))
        runCurrent()
        vm.onEvent(MapsEvent.RetryMaps)
        runCurrent()
        assertTrue(vm.state.value.remote)
        assertNull(vm.state.value.planFile)
        assertNull(vm.state.value.routeFailure)
        cpu.hold = true
        vm.onEvent(MapsEvent.ShowRoom("564*"))
        runCurrent()
        assertEquals(1, cpu.pending)
        assertTrue(vm.state.value.routeLoading)
        assertTrue(vm.state.value.remote)
        vm.onEvent(MapsEvent.RetryMaps)
        runCurrent()
        assertEquals(2, cpu.pending)
        cpu.releaseAll()
        runCurrent()
        repeat(2) {
            val state = vm.state.value
            assertFalse("Physical retry must not inherit the remote display", state.remote)
            assertNull(state.remoteNote)
            assertFalse(state.routeLoading)
            assertNotNull(state.route)
            assertEquals(state.presentation!!.steps.first().id, state.activeStepId)
            assertEquals(data.rasters.getValue(FloorKey("УЛК", 1)).file, state.planFile)
            assertEquals("УЛК", state.building)
            assertEquals(1, state.floor)
            assertNotNull(state.stackRasterRevision)
            vm.onEvent(MapsEvent.RetryMaps)
            runCurrent()
        }
    }

    @Test fun toNext_final_io_failure_cannot_override_next_step_or_replace_last_good_catalog() = fixture { vm, data, cpu ->
        val before = vm.state.value
        data.barrierCall = data.catalogCalls + 2 // Computation succeeds; final publication catalog suspends.
        data.failPublication = true
        vm.onEvent(MapsEvent.ToNext)
        runCurrent()
        assertTrue(data.entered.isCompleted)
        assertTrue(vm.state.value.routeLoading)
        assertNull(vm.state.value.route)
        data.release.complete(Unit)
        runCurrent()
        val failed = vm.state.value
        assertEquals("final map publication", data.errors.single().message)
        assertFalse(failed.routeLoading)
        assertEquals("maps_route_load_failed", failed.routeFailure)
        val last = failed.presentation!!.steps.last().id
        data.barrierCall = data.catalogCalls + 1
        data.entered = CompletableDeferred()
        data.release = CompletableDeferred()
        data.failPublication = false
        val emissions = mutableListOf<MapsUiState>()
        backgroundScope.launch(UnconfinedTestDispatcher(testScheduler)) { vm.state.collect { emissions += it } }
        vm.onEvent(MapsEvent.SelectRouteStep(last))
        runCurrent()
        assertTrue(data.entered.isCompleted)
        assertSame("Step/floor/file must wait for the same IO transaction", failed, vm.state.value)
        data.release.complete(Unit)
        runCurrent()
        val selected = vm.state.value
        assertEquals("Failed request must not reapply its first step", last, selected.activeStepId)
        assertEquals("УЛК", selected.building)
        assertEquals(5, selected.floor)
        assertEquals(data.rasters.getValue(FloorKey("УЛК", 5)).file, selected.planFile)
        assertEquals(HighlightUi(data.rect, "564"), selected.highlight)
        assertEquals(2, emissions.size)
        assertSame(failed, emissions[0])
        assertSame(selected, emissions[1])
        assertSame(before.rasterCatalog, failed.rasterCatalog)
        assertSame(before.floorFiles, failed.floorFiles)
        assertSame(before.stackRasterRevision, failed.stackRasterRevision)
        assertEquals(before.planFile, failed.planFile)
        assertNull(failed.activeStepId)
        val computations = cpu.calls
        vm.onEvent(MapsEvent.Fullscreen(true))
        vm.onEvent(MapsEvent.ToggleStack)
        vm.onEvent(MapsEvent.PickFloor(1))
        runCurrent()
        assertSame(selected.route, vm.state.value.route)
        assertSame(selected.presentation, vm.state.value.presentation)
        assertEquals(last, vm.state.value.activeStepId)
        assertEquals(computations, cpu.calls)
        vm.onEvent(MapsEvent.RetryMaps)
        runCurrent()
        assertFalse(vm.state.value.routeLoading)
        assertNull(vm.state.value.routeFailure)
        assertEquals(last, vm.state.value.activeStepId)
    }

    @Test fun cancelled_final_io_finishing_after_new_request_cannot_clear_its_selection() = fixture { vm, data, _ ->
        data.barrierCall = data.catalogCalls + 2
        data.finishCancelledIo = true
        vm.onEvent(MapsEvent.ToNext)
        runCurrent()
        assertTrue(data.entered.isCompleted)
        vm.onEvent(MapsEvent.ShowRoom("564*"))
        runCurrent()
        val first = vm.state.value
        assertFalse(first.routeLoading)
        val last = first.presentation!!.steps.last().id
        vm.onEvent(MapsEvent.SelectRouteStep(last))
        runCurrent()
        val newer = vm.state.value
        assertEquals(last, newer.activeStepId)
        assertEquals(5, newer.floor)
        data.release.complete(Unit)
        runCurrent()
        assertSame("Old completion must not publish or clear the newer selection", newer, vm.state.value)
        assertTrue(data.errors.isEmpty())
        vm.onEvent(MapsEvent.PreviousRouteStep)
        runCurrent()
        assertEquals(first.presentation.steps[1].id, vm.state.value.activeStepId)
        assertSame(newer.route, vm.state.value.route)
    }
}
