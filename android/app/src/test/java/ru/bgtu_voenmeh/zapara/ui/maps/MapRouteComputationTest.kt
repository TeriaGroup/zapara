package ru.bgtu_voenmeh.zapara.ui.maps

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.withContext
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.campus.*
import kotlin.coroutines.CoroutineContext

@OptIn(ExperimentalCoroutinesApi::class)
class MapRouteComputationTest {
    private val a = Node("a", "room", "ГК", 1, .1, .1, room = "101")
    private val b = Node("b", "room", "ГК", 1, .8, .1, room = "102")
    private val graph = CampusGraph(1, listOf("ГК"), listOf(a, b), listOf(Edge("a", "b", "walk", 10.0, false)))
    private fun input(g: CampusGraph = graph, from: String = "a", to: String = "b") =
        MapRouteInput.capture(g, from, to, null, null, null)

    @Test fun real_route_and_presentation_run_after_io_on_injected_dispatcher() = runTest {
        val events = mutableListOf<String>()
        val delegate = StandardTestDispatcher(testScheduler)
        fun recording(label: String) = object : CoroutineDispatcher() {
            override fun dispatch(context: CoroutineContext, block: Runnable) {
                events += label
                delegate.dispatch(context, block)
            }
        }
        val requests = MapRouteComputations(recording("route-dispatch"), recording("io-dispatch"))
        var result: MapRouteResult? = null
        launch {
            result = requests.compute(input()) { events += "catalog"; emptyMap() }
            events += "published"
        }
        assertNull(result)
        runCurrent()
        assertEquals(listOf("io-dispatch", "catalog", "route-dispatch", "published"), events)
        assertEquals(10, result?.route?.seconds)
        assertEquals(1, result?.presentation?.steps?.size)
        assertTrue(RouteProblem.MissingMap in result!!.presentation!!.problems)
        assertNull(result?.failure)
    }

    @Test fun reverse_completion_cannot_publish_cancelled_older_route() = runTest {
        val requests = MapRouteComputations(StandardTestDispatcher(testScheduler), StandardTestDispatcher(testScheduler))
        val release = CompletableDeferred<Unit>()
        val published = mutableListOf<String>()
        val old = launch {
            requests.compute(input()) { withContext(NonCancellable) { release.await() }; emptyMap() }
            published += "old"
        }
        runCurrent()
        launch { requests.compute(input(from = "b", to = "a")) { emptyMap() }; published += "new" }
        runCurrent()
        assertEquals(listOf("new"), published)
        release.complete(Unit)
        runCurrent()
        assertTrue(old.isCancelled)
        assertEquals(listOf("new"), published)
    }

    @Test fun snapshot_copies_mutable_graph_inputs_before_scheduling() = runTest {
        val nodes = graph.nodes.toMutableList()
        val edges = graph.edges.toMutableList()
        val captured = input(graph.copy(nodes = nodes, edges = edges))
        nodes.clear()
        edges.clear()
        val result = MapRouteComputations(StandardTestDispatcher(testScheduler), StandardTestDispatcher(testScheduler))
            .compute(captured) { emptyMap() }
        assertNotNull(result.route)
        assertEquals("a", result.from?.id)
    }

    @Test fun failures_distinguish_missing_graph_unknown_and_unreachable() = runTest {
        val requests = MapRouteComputations(StandardTestDispatcher(testScheduler), StandardTestDispatcher(testScheduler))
        assertEquals("maps_route_graph_missing", requests.compute(input(CampusGraph.empty)) { emptyMap() }.failure)
        assertEquals("maps_route_unknown", requests.compute(input(to = "missing")) { emptyMap() }.failure)
        assertEquals("maps_route_unreachable", requests.compute(input(graph.copy(edges = emptyList()))) { emptyMap() }.failure)
    }

    @Test fun ordinary_io_failure_does_not_poison_next_request() = runTest {
        val requests = MapRouteComputations(StandardTestDispatcher(testScheduler), StandardTestDispatcher(testScheduler))
        try {
            requests.compute(input()) { throw java.io.IOException("synthetic") }
            fail("IO failure must propagate")
        } catch (_: java.io.IOException) { }
        assertNotNull(requests.compute(input()) { emptyMap() }.route)
    }

    @Test fun cancellation_during_io_has_no_result_and_next_request_succeeds() = runTest {
        val requests = MapRouteComputations(StandardTestDispatcher(testScheduler), StandardTestDispatcher(testScheduler))
        val gate = CompletableDeferred<Unit>()
        var published = false
        val job = launch { requests.compute(input()) { gate.await(); emptyMap() }; published = true }
        runCurrent()
        job.cancel()
        gate.complete(Unit)
        runCurrent()
        assertFalse(published)
        assertNotNull(requests.compute(input()) { emptyMap() }.route)
    }

    @Test fun same_endpoint_is_arrived_and_disconnected_start_keeps_offline_entrance_fallback() = runTest {
        val requests = MapRouteComputations(StandardTestDispatcher(testScheduler), StandardTestDispatcher(testScheduler))
        assertTrue(requests.compute(input(to = "a")) { emptyMap() }.presentation!!.arrived)
        val entrance = a.copy(id = "gk.main", kind = "entrance")
        val fallback = graph.copy(nodes = listOf(a, b, entrance), edges = listOf(Edge(entrance.id, b.id, "walk", 5.0, false)))
        val result = requests.compute(input(fallback)) { emptyMap() }
        assertEquals(entrance, result.from)
        assertEquals(a, result.guessed)
        assertNotNull(result.route)
    }
}
