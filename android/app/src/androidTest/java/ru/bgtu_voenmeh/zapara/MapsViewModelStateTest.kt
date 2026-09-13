package ru.bgtu_voenmeh.zapara

import android.app.Application
import android.content.Context
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.ViewModelStore
import androidx.room.Room
import androidx.test.ext.junit.runners.AndroidJUnit4
import kotlin.coroutines.CoroutineContext
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import kotlinx.coroutines.withContext
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.data.api.ApiRefreshCoordinator
import ru.bgtu_voenmeh.zapara.data.api.RoomTimetableStore
import ru.bgtu_voenmeh.zapara.data.api.UrlConnectionTransport
import ru.bgtu_voenmeh.zapara.data.db.ZaparaDatabase
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileDescriptor
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork
import ru.bgtu_voenmeh.zapara.ui.maps.*

/** Compiled offline; execution requires the separately approved device/restoration gate. */
@OptIn(ExperimentalCoroutinesApi::class)
@RunWith(AndroidJUnit4::class)
class MapsViewModelStateTest {
    private class MapTestApplication(base: Context) : Application() {
        init { attachBaseContext(base) }
        override fun getApplicationContext(): Context = this
    }

    private class GateDispatcher(private val delegate: CoroutineDispatcher) : CoroutineDispatcher() {
        var paused = false
        var dispatches = 0
        private val queued = ArrayDeque<Pair<CoroutineContext, Runnable>>()
        val pending: Int get() = queued.size
        override fun dispatch(context: CoroutineContext, block: Runnable) {
            dispatches++
            if (paused) queued.addLast(context to block) else delegate.dispatch(context, block)
        }
        fun releaseNewest() { queued.removeLast().let { delegate.dispatch(it.first, it.second) } }
        fun releaseAll() {
            paused = false
            while (queued.isNotEmpty()) queued.removeFirst().let { delegate.dispatch(it.first, it.second) }
        }
    }

    private data class Fixture(val vm: MapsViewModel, val container: AppContainer, val cpu: GateDispatcher, val io: GateDispatcher)

    private fun withVm(block: suspend TestScope.(Fixture) -> Unit) = runTest {
        val main = StandardTestDispatcher(testScheduler)
        val cpu = GateDispatcher(StandardTestDispatcher(testScheduler))
        val io = GateDispatcher(StandardTestDispatcher(testScheduler))
        Dispatchers.setMain(main)
        val context = MapTestContext()
        val owner = ViewModelStore()
        var container: AppContainer? = null
        try {
            val created = withContext(Dispatchers.IO) {
                val app = MapTestApplication(context)
                val db = Room.inMemoryDatabaseBuilder(app, ZaparaDatabase::class.java).build()
                try {
                    val store = RoomTimetableStore(db)
                    val work = ProfileWork()
                    val repo = ScheduleRepository(db, store, work)
                    val api = ApiRefreshCoordinator(store, work, null, UrlConnectionTransport())
                    AppContainer(app, ProfileDescriptor.guest(), db, repo, work, api).also {
                        it.mapStore.saveLastEntrance("ulk.entrance.main")
                    }
                } catch (error: Throwable) { db.close(); throw error }
            }
            container = created
            val factory = object : ViewModelProvider.Factory {
                @Suppress("UNCHECKED_CAST")
                override fun <T : androidx.lifecycle.ViewModel> create(modelClass: Class<T>): T =
                    MapsViewModel(created, "564*", cpu, io) as T
            }
            val vm = ViewModelProvider(owner, factory)[MapsViewModel::class.java]
            runCurrent()
            assertNotNull("Real bundled route fixture must load", vm.state.value.route)
            assertTrue(vm.state.value.presentation!!.steps.size > 2)
            block(Fixture(vm, created, cpu, io))
        } finally {
            owner.clear()
            cpu.releaseAll()
            io.releaseAll()
            runCurrent()
            withContext(Dispatchers.IO) { container?.close(); context.close() }
            Dispatchers.resetMain()
        }
    }

    @Test fun selection_manual_floor_and_modes_share_route_without_rerouting() = withVm { f ->
        val vm = f.vm
        val original = vm.state.value
        val id = original.presentation!!.steps.last().id
        val computations = f.cpu.dispatches
        vm.onEvent(MapsEvent.OpenRouteSteps)
        vm.onEvent(MapsEvent.Fullscreen(true))
        vm.onEvent(MapsEvent.SelectRouteStep(id))
        runCurrent()
        val selected = vm.state.value
        assertSame(original.route, selected.route)
        assertSame(original.presentation, selected.presentation)
        assertEquals(id, selected.activeStepId)
        assertTrue(selected.fullscreen)
        assertFalse(selected.stepsOpen)
        assertEquals(selected.rasterCatalog[FloorKey(selected.building, selected.floor)]?.file, selected.planFile)
        val coords = withContext(Dispatchers.IO) { f.container.mapStore.findCoords(selected.building, selected.floor, "564") }
        assertEquals(coords, selected.highlight?.rect)
        assertNotNull(selected.stackRasterRevision)
        vm.onEvent(MapsEvent.Transform(2f))
        vm.onEvent(MapsEvent.SelectRouteStep(id))
        runCurrent()
        assertEquals(selected.fitGeneration, vm.state.value.fitGeneration)
        assertEquals(2f, vm.state.value.zoom)
        vm.onEvent(MapsEvent.PickFloor(1))
        runCurrent()
        assertEquals(id, vm.state.value.activeStepId)
        assertSame(original.route, vm.state.value.route)
        vm.onEvent(MapsEvent.PickBuilding(0))
        runCurrent()
        assertEquals("ГК", vm.state.value.building)
        assertEquals(id, vm.state.value.activeStepId)
        assertSame(original.route, vm.state.value.route)
        assertEquals(vm.state.value.rasterCatalog[FloorKey("ГК", 1)]?.file, vm.state.value.planFile)
        vm.onEvent(MapsEvent.ToggleStack)
        vm.onEvent(MapsEvent.SelectRouteStep(id))
        runCurrent()
        assertEquals(selected.floor, vm.state.value.floor)
        assertFalse(vm.state.value.showStack)
        assertTrue(vm.state.value.fitGeneration > selected.fitGeneration)
        vm.onEvent(MapsEvent.PreviousRouteStep)
        runCurrent()
        assertSame(original.route, vm.state.value.route)
        assertEquals(computations, f.cpu.dispatches)
    }

    @Test fun queued_swap_is_hidden_and_newer_endpoint_wins_reverse_cpu_order() = withVm { f ->
        val vm = f.vm
        val original = vm.state.value.route
        f.cpu.paused = true
        vm.onEvent(MapsEvent.SwapEnds)
        runCurrent()
        assertTrue(vm.state.value.routeLoading)
        assertNull(vm.state.value.route)
        assertNull(vm.state.value.presentation)
        assertTrue(vm.state.value.path.isEmpty())
        assertTrue(f.cpu.pending > 0)
        vm.onEvent(MapsEvent.OpenTo)
        vm.onEvent(MapsEvent.PickPlace("ulk.room.564"))
        runCurrent()
        f.cpu.releaseNewest()
        runCurrent()
        val newest = vm.state.value
        assertTrue(newest.presentation!!.arrived)
        assertFalse(newest.routeLoading)
        assertNotSame(original, newest.route)
        f.cpu.releaseAll()
        runCurrent()
        assertSame(newest.route, vm.state.value.route)
        assertNull(vm.state.value.routeFailure)
    }

    @Test fun cancelled_entrance_io_preserves_coherent_endpoints_and_manual_priority() = withVm { f ->
        val vm = f.vm
        val before = vm.state.value
        f.io.paused = true
        vm.onEvent(MapsEvent.OpenFrom)
        vm.onEvent(MapsEvent.PickPlace("ulk.entrance.hostel"))
        runCurrent()
        assertEquals(before.fromLabel, vm.state.value.fromLabel)
        assertSame(before.route, vm.state.value.route)
        vm.onEvent(MapsEvent.PickFloor(2))
        runCurrent()
        f.container.notifyDataChanged()
        runCurrent()
        f.io.releaseAll()
        runCurrent()
        assertEquals(2, vm.state.value.floor)
        assertSame(before.route, vm.state.value.route)
        assertEquals(before.fromLabel, vm.state.value.fromLabel)
        assertEquals(before.toLabel, vm.state.value.toLabel)
        assertEquals(before.activeStepId, vm.state.value.activeStepId)
    }

    @Test fun retry_preserves_valid_step_and_modal_exclusion_keeps_fullscreen() = withVm { f ->
        val vm = f.vm
        val id = vm.state.value.presentation!!.steps.last().id
        vm.onEvent(MapsEvent.SelectRouteStep(id))
        runCurrent()
        val previous = vm.state.value
        vm.onEvent(MapsEvent.RetryMaps)
        runCurrent()
        assertEquals(id, vm.state.value.activeStepId)
        assertEquals(previous.fromLabel, vm.state.value.fromLabel)
        assertEquals(previous.toLabel, vm.state.value.toLabel)
        assertEquals(previous.route, vm.state.value.route)
        assertNotEquals(previous.stackRasterRevision, vm.state.value.stackRasterRevision)
        vm.onEvent(MapsEvent.Fullscreen(true))
        vm.onEvent(MapsEvent.OpenRouteSteps)
        vm.onEvent(MapsEvent.OpenFrom)
        assertFalse(vm.state.value.stepsOpen)
        assertNotNull(vm.state.value.picker)
        vm.onEvent(MapsEvent.OpenRouteSteps)
        assertNull(vm.state.value.picker)
        vm.onEvent(MapsEvent.CloseRouteSteps)
        assertTrue(vm.state.value.fullscreen)
    }

    @Test fun unknown_destination_retry_and_remote_keep_recoverable_offline_state() = withVm { f ->
        val vm = f.vm
        vm.onEvent(MapsEvent.ShowRoom("199999"))
        runCurrent()
        val failure = vm.state.value.routeFailure
        assertEquals(f.container.copy.get("maps_route_unknown"), failure)
        assertNull(vm.state.value.route)
        assertTrue(vm.state.value.path.isEmpty())
        assertTrue(vm.state.value.rasterCatalog.isNotEmpty())
        vm.onEvent(MapsEvent.RetryMaps)
        runCurrent()
        assertEquals(failure, vm.state.value.routeFailure)
        vm.onEvent(MapsEvent.ShowRoom("дистанционно"))
        runCurrent()
        vm.onEvent(MapsEvent.RetryMaps)
        runCurrent()
        assertTrue(vm.state.value.remote)
        assertNull(vm.state.value.planFile)
        assertNull(vm.state.value.routeFailure)
        vm.onEvent(MapsEvent.ShowRoom("564*"))
        runCurrent()
        assertFalse(vm.state.value.remote)
        assertNotNull(vm.state.value.route)
    }
}
