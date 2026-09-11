package ru.bgtu_voenmeh.zapara.ui.maps

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.withContext
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.campus.CampusGraph
import ru.bgtu_voenmeh.zapara.data.campus.CampusRouter
import ru.bgtu_voenmeh.zapara.data.campus.Edge
import ru.bgtu_voenmeh.zapara.data.campus.Node
import ru.bgtu_voenmeh.zapara.ui.XmlCopy

class MapFromSelectionTest {
    private val a = Node("A", "room", "ГК", 1, 0.1, 0.1, room = "101")
    private val b = Node("B", "room", "ГК", 2, 0.8, 0.8, room = "201")
    private val c = Node("C", "entrance", "ГК", 1, 0.9, 0.1, label = "Вход C")
    private val d = c.copy(id = "D", x = 0.0, label = "Вход D")
    private val s1 = Node("S1", "stair", "ГК", 1, 0.5, 0.5, group = "S")
    private val s2 = s1.copy(id = "S2", floor = 2)
    private val graph = CampusGraph(1, listOf("ГК"), listOf(a, b, c, d, s1, s2), listOf(
        Edge("A", "S1", "walk", 10.0, false), Edge("C", "S1", "walk", 10.0, false),
        Edge("D", "S1", "walk", 10.0, false),
        Edge("S1", "S2", "stair_up", 10.0, false), Edge("S2", "B", "walk", 10.0, false)
    ))

    @Test fun pick_floor_cancels_entrance_io_without_mixing_c_labels_with_a_to_b_geometry() = runTest {
        val requests = MapLoadRequests(this)
        var selection = MapFromSelection(a.id, d.id, a.room)
        val oldRoute = requireNotNull(CampusRouter.find(graph, a.id, b.id).route)
        var route = oldRoute
        val state = MutableStateFlow(MapsUiState(route = oldRoute))
        val entered = CompletableDeferred<Unit>()
        val release = CompletableDeferred<Unit>()
        val entrance = requests.launch {
            requests.selectFromPlace(selection, c, rememberEntrance = {
                withContext(NonCancellable) { entered.complete(Unit); release.await(); c.id }
            }, commit = { selection = it })
            // Same no-suspension boundary as applyPlace -> revealNode -> computeRoute.
            route = requireNotNull(CampusRouter.find(graph, selection.fromId!!, b.id).route)
        }
        entered.await()
        requests.launch {
            requests.ensureCurrent()
            val from = graph.nodes.single { it.id == selection.fromId }
            val room = MapsComposer.highlightRoom("ГК", 1, b, from)
            state.value = state.value.copy(mode = MapMode.Manual, floor = 1, route = route,
                fromLabel = MapsComposer.placeLabel(from), toLabel = MapsComposer.placeLabel(b),
                path = MapsComposer.floorPathStrokes(route, "ГК", 1),
                routeSteps = MapsComposer.formatRouteSteps(route, XmlCopy),
                highlight = room?.let { HighlightUi(ru.bgtu_voenmeh.zapara.data.CoordsRect(0.1, 0.1, 0.1, 0.1), it) })
        }.join()
        release.complete(Unit)
        entrance.join()
        assertTrue(entrance.isCancelled)
        assertEquals("cancelled entrance must retain coherent A selection", a.id, selection.fromId)
        assertEquals(d.id, selection.lastEntranceId)
        assertEquals(a.room, selection.prevRoomKey)
        assertSame(oldRoute, state.value.route)
        assertEquals(MapsComposer.placeLabel(a), state.value.fromLabel)
        assertEquals(MapsComposer.placeLabel(b), state.value.toLabel)
        assertEquals(MapsComposer.floorPathStrokes(oldRoute, "ГК", 1), state.value.path)
        assertEquals(MapsComposer.formatRouteSteps(oldRoute, XmlCopy), state.value.routeSteps)
        assertEquals("floor 1 must still highlight A, not the cancelled entrance",
            HighlightUi(ru.bgtu_voenmeh.zapara.data.CoordsRect(0.1, 0.1, 0.1, 0.1), "101"), state.value.highlight)
    }

    @Test fun current_entrance_commits_once_after_io_and_owner_cancellation_never_commits() = runTest {
        val owner = Job()
        val requests = MapLoadRequests(CoroutineScope(coroutineContext + owner))
        val initial = MapFromSelection(a.id, d.id, a.room)
        var selection = initial
        var commits = 0
        val entered = CompletableDeferred<Unit>()
        val release = CompletableDeferred<Unit>()
        val request = requests.launch {
            requests.selectFromPlace(initial, c, {
                withContext(NonCancellable) { entered.complete(Unit); release.await(); c.id }
            }) { selection = it; commits++ }
        }
        entered.await()
        owner.cancel()
        release.complete(Unit)
        request.join()
        assertTrue(request.isCancelled)
        assertEquals(initial, selection)
        assertEquals(0, commits)
        val live = MapLoadRequests(this)
        live.launch {
            live.selectFromPlace(initial, c, { c.id }) { selection = it; commits++ }
        }.join()
        assertEquals(MapFromSelection(c.id, c.id, null), selection)
        assertEquals(1, commits)
    }

    @Test fun newer_selection_rejects_old_noncooperative_generation_at_commit() = runTest {
        val requests = MapLoadRequests(this)
        var selection = MapFromSelection(a.id, d.id, a.room)
        val entered = CompletableDeferred<Unit>()
        val release = CompletableDeferred<Unit>()
        val old = requests.launch {
            withContext(NonCancellable) {
                requests.selectFromPlace(selection, c, {
                    entered.complete(Unit); release.await(); c.id
                }) { selection = it }
            }
        }
        entered.await()
        requests.launch {
            requests.selectFromPlace(selection, b, { error("room must not persist entrance") }) { selection = it }
        }.join()
        val newer = selection
        release.complete(Unit)
        old.join()
        assertTrue(old.isCancelled)
        assertEquals(MapFromSelection(b.id, d.id, b.room), selection)
        assertSame(newer, selection)
    }
}
