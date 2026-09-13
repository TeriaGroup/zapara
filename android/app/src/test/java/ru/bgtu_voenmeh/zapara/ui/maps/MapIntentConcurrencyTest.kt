package ru.bgtu_voenmeh.zapara.ui.maps

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.withContext
import org.junit.Assert.*
import org.junit.Test

class MapIntentConcurrencyTest {
    @Test fun pending_pick_floor_survives_container_refresh_of_next_lesson() =
        pendingSelection(MapMode.NextLesson, MapMode.Manual)

    @Test fun pending_show_room_survives_container_refresh_of_next_lesson() =
        pendingSelection(MapMode.NextLesson, MapMode.Lesson)

    @Test fun startup_room_arg_survives_container_refresh_before_first_publication() =
        pendingSelection(MapMode.None, MapMode.Lesson)

    private fun pendingSelection(initial: MapMode, selected: MapMode) = runTest {
        val requests = MapLoadRequests(this)
        val state = MutableStateFlow(MapsUiState(mode = initial))
        val entered = CompletableDeferred<Unit>()
        val release = CompletableDeferred<Unit>()
        var automaticStarts = 0
        val manual = requests.launch {
            withContext(NonCancellable) { entered.complete(Unit); release.await() }
            requests.ensureCurrent()
            state.value = state.value.copy(mode = selected, floor = 3)
        }
        entered.await()
        assertEquals(initial, state.value.mode) // exactly the reviewer's stale published mode
        requests.refresh(state.value.mode) {
            automaticStarts++
            requests.ensureCurrent()
            state.value = state.value.copy(mode = MapMode.NextLesson, floor = 1)
        }?.join() // automatic request completes BEFORE held explicit IO is released
        release.complete(Unit)
        manual.join()
        assertEquals("container event must not supersede explicit selection", 0, automaticStarts)
        assertFalse(manual.isCancelled)
        assertEquals(selected, state.value.mode)
        assertEquals(3, state.value.floor)
    }

    @Test fun explicit_to_next_supersedes_pending_manual_and_auto_refresh_resumes_after_completion() = runTest {
        val requests = MapLoadRequests(this)
        val state = MutableStateFlow(MapsUiState(mode = MapMode.NextLesson))
        val entered = CompletableDeferred<Unit>()
        val release = CompletableDeferred<Unit>()
        val manual = requests.launch {
            withContext(NonCancellable) { entered.complete(Unit); release.await() }
            requests.ensureCurrent()
            state.value = state.value.copy(mode = MapMode.Manual, floor = 4)
        }
        entered.await()
        requests.launch { // explicit ToNext uses launch, not refresh
            requests.ensureCurrent()
            state.value = state.value.copy(mode = MapMode.NextLesson, floor = 2)
        }.join()
        release.complete(Unit)
        manual.join()
        assertTrue(manual.isCancelled)
        assertEquals(2, state.value.floor)
        var refreshed = false
        requests.refresh(state.value.mode) { refreshed = true }?.join()
        assertTrue(refreshed)
        assertNull(requests.refresh(MapMode.Manual) { fail("manual auto refresh") })
        assertNull(requests.refresh(MapMode.Lesson) { fail("lesson auto refresh") })
    }

    @Test fun manual_intent_supersedes_running_auto_and_later_auto_cannot_take_it_back() = runTest {
        val requests = MapLoadRequests(this)
        val entered = CompletableDeferred<Unit>()
        val release = CompletableDeferred<Unit>()
        var floor = 1
        val automatic = requireNotNull(requests.refresh(MapMode.NextLesson) {
            withContext(NonCancellable) { entered.complete(Unit); release.await() }
            requests.ensureCurrent()
            floor = 2
        })
        entered.await()
        val manualRelease = CompletableDeferred<Unit>()
        val manual = requests.launch {
            manualRelease.await()
            requests.ensureCurrent()
            floor = 4
        }
        assertNull(requests.refresh(MapMode.NextLesson) { floor = 3 })
        manualRelease.complete(Unit)
        manual.join()
        release.complete(Unit)
        automatic.join()
        assertTrue(automatic.isCancelled)
        assertEquals(4, floor)
    }
}
