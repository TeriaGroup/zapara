package ru.bgtu_voenmeh.zapara.ui.maps

import androidx.compose.runtime.AbstractApplier
import androidx.compose.runtime.BroadcastFrameClock
import androidx.compose.runtime.Composition
import androidx.compose.runtime.Recomposer
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.snapshots.Snapshot
import java.io.File
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.withContext
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import ru.bgtu_voenmeh.zapara.data.MapResolve

@OptIn(kotlinx.coroutines.ExperimentalCoroutinesApi::class)
class MapRasterPublicationTest {
    @get:Rule val temporary = TemporaryFolder()

    @Test fun published_length_only_refresh_restarts_state_flow_producer() = refresh { file ->
        val modified = file.lastModified()
        val length = file.length()
        file.appendBytes(byteArrayOf(2))
        assertTrue(file.setLastModified(modified))
        assertEquals(modified, file.lastModified())
        assertEquals(length + 1, file.length())
    }

    @Test fun published_mtime_only_refresh_restarts_state_flow_producer() = refresh { file ->
        val length = file.length()
        assertTrue(file.setLastModified(file.lastModified() + 10_000))
        assertEquals(length, file.length())
    }

    private fun refresh(change: (File) -> Unit) = runTest {
        val file = temporary.newFile(MapResolve.MAP_FILES.getValue("ГК" to 1)).apply { writeBytes(byteArrayOf(1)) }
        val state = MutableStateFlow(MapsUiState(fullscreen = true, showStack = true, zoom = 2f))
        val requests = MapLoadRequests(this)
        val caller = Thread.currentThread()
        suspend fun publish() {
            requests.launch {
                val publication = MapRasterPublication.capture("ВЦ") { shown ->
                    assertNotSame(caller, Thread.currentThread())
                    assertEquals("ГК", shown)
                    mapOf(1 to File(file.path))
                }
                requests.ensureCurrent()
                state.value = publication.applyTo(state.value)
            }.join()
        }
        publish()
        val initial = state.value
        val frames = BroadcastFrameClock()
        val recomposer = Recomposer(coroutineContext + frames)
        val runner = launch(frames) { recomposer.runRecomposeAndApplyChanges() }
        val composition = Composition(NoNodes(), recomposer)
        var starts = 0
        var visible: Int? = null
        try {
            composition.setContent {
                val current = state.collectAsState().value
                visible = produceThumbnails(current.building, current.floors, current.floorFiles,
                    current.stackRasterRevision) { ++starts }
            }
            fun settle() {
                repeat(4) { Snapshot.sendApplyNotifications(); runCurrent(); frames.sendFrame(0); runCurrent() }
            }
            settle()
            assertEquals(1, visible)
            publish()
            settle()
            assertEquals(initial, state.value)
            assertEquals(1, starts)
            change(file)
            publish()
            settle()
            assertEquals(initial.floorFiles, state.value.floorFiles)
            assertEquals(2, starts)
            assertNotEquals(initial.stackRasterRevision, state.value.stackRasterRevision)
            assertEquals(initial.copy(stackRasterRevision = state.value.stackRasterRevision), state.value)
            assertEquals(2, starts)
            assertEquals(2, visible)
        } finally { composition.dispose(); recomposer.cancel(); runner.join() }
    }

    @Test fun cancelled_older_snapshot_cannot_replace_newer_building() = runTest {
        val source = mutableMapOf(1 to temporary.newFile(MapResolve.MAP_FILES.getValue("ГК" to 1)))
        val old = MapRasterPublication.capture("ГК") { source }
        source.clear()
        assertEquals(1, old.files.size)
        try {
            (old.files as MutableMap<Int, File>).clear()
            fail("published map must reject mutation")
        } catch (_: UnsupportedOperationException) { }
        val next = MapRasterPublication.capture("УЛК") {
            mapOf(1 to temporary.newFile(MapResolve.MAP_FILES.getValue("УЛК" to 1)))
        }
        val state = MutableStateFlow(MapsUiState())
        val requests = MapLoadRequests(this)
        val entered = CompletableDeferred<Unit>()
        val release = CompletableDeferred<Unit>()
        val first = requests.launch {
            withContext(NonCancellable) {
                entered.complete(Unit)
                release.await()
                requests.ensureCurrent()
                state.value = old.applyTo(state.value)
            }
        }
        entered.await()
        requests.launch {
            requests.ensureCurrent()
            state.value = next.applyTo(state.value)
        }.join()
        release.complete(Unit)
        first.join()
        assertTrue(first.isCancelled)
        assertEquals("УЛК", state.value.building)
        assertSame(next.files, state.value.floorFiles)
        assertSame(next.revision, state.value.stackRasterRevision)
    }

    @Test fun owner_cancellation_prevents_pending_snapshot_publication() = runTest {
        val owner = Job()
        val requests = MapLoadRequests(CoroutineScope(coroutineContext + owner))
        val entered = CompletableDeferred<Unit>()
        val release = CompletableDeferred<Unit>()
        var published = false
        val job = requests.launch {
            withContext(NonCancellable) { entered.complete(Unit); release.await() }
            requests.ensureCurrent()
            published = true
        }
        entered.await()
        owner.cancel()
        release.complete(Unit)
        job.join()
        assertTrue(job.isCancelled)
        assertFalse(published)
    }

    private class NoNodes : AbstractApplier<Unit>(Unit) {
        override fun insertTopDown(index: Int, instance: Unit) = Unit
        override fun insertBottomUp(index: Int, instance: Unit) = Unit
        override fun remove(index: Int, count: Int) = Unit
        override fun move(from: Int, to: Int, count: Int) = Unit
        override fun onClear() = Unit
    }
}
