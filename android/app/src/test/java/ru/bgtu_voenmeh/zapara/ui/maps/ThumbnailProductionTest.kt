package ru.bgtu_voenmeh.zapara.ui.maps

import androidx.compose.runtime.AbstractApplier
import androidx.compose.runtime.BroadcastFrameClock
import androidx.compose.runtime.Composition
import androidx.compose.runtime.Recomposer
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.snapshots.Snapshot
import java.io.File
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.NonCancellable
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
class ThumbnailProductionTest {
    @get:Rule val temporary = TemporaryFolder()

    @Test fun capture_reads_metadata_off_calling_thread_and_keeps_snapshot_immutable() = runTest {
        val caller = Thread.currentThread()
        val backing = temporary.newFile(MapResolve.MAP_FILES.getValue("ГК" to 1))
        val file = object : File(backing.path) {
            override fun isFile(): Boolean { assertNotSame(caller, Thread.currentThread()); return super.isFile() }
            override fun length(): Long { assertNotSame(caller, Thread.currentThread()); return super.length() }
            override fun lastModified(): Long { assertNotSame(caller, Thread.currentThread()); return super.lastModified() }
        }
        val files = mutableMapOf(1 to file)
        val before = StackRasterRevision.capture("ВЦ", files)
        val hash = before.hashCode()
        files.clear()
        backing.appendBytes(byteArrayOf(1))
        assertEquals(hash, before.hashCode())
        assertNotEquals(before, StackRasterRevision.capture("ГК", mapOf(1 to backing)))
        assertEquals(1, before.keysFor("ГК", listOf(1)).size)
    }

    @Test fun same_path_length_change_restarts_producer() = checkRefresh { file ->
        val modified = file.lastModified()
        val length = file.length()
        file.appendBytes(byteArrayOf(2))
        assertTrue(file.setLastModified(modified))
        assertEquals(modified, file.lastModified())
        assertEquals(length + 1, file.length())
    }

    @Test fun same_path_mtime_change_restarts_producer() = checkRefresh { file ->
        assertTrue(file.setLastModified(file.lastModified() + 10_000))
    }

    private fun checkRefresh(change: (File) -> Unit) = runTest {
        val file = temporary.newFile(MapResolve.MAP_FILES.getValue("ГК" to 1)).apply { writeBytes(byteArrayOf(1)) }
        val files = mapOf(1 to file)
        val initial = StackRasterRevision.capture("ГК", files)
        val revision = mutableStateOf(initial)
        val recompose = mutableStateOf(0)
        val frames = BroadcastFrameClock()
        val recomposer = Recomposer(coroutineContext + frames)
        val runner = launch(frames) { recomposer.runRecomposeAndApplyChanges() }
        val composition = Composition(NoNodes(), recomposer)
        var starts = 0
        var visible: Int? = null
        try {
            composition.setContent {
                recompose.value
                visible = produceThumbnails("ГК", listOf(1), files, revision.value) { ++starts }
            }
            suspend fun settle() {
                repeat(4) { Snapshot.sendApplyNotifications(); runCurrent(); frames.sendFrame(0); runCurrent() }
            }
            settle()
            assertEquals(1, starts)
            assertEquals(1, visible)
            revision.value = StackRasterRevision.capture("ГК", mapOf(1 to File(file.path)))
            recompose.value++
            settle()
            assertEquals("equal metadata must keep producer", 1, starts)
            change(file)
            revision.value = StackRasterRevision.capture("ГК", files)
            assertNotEquals(initial, revision.value)
            settle()
            assertEquals("same paths with changed metadata must restart producer", 2, starts)
            assertEquals(2, visible)
        } finally { composition.dispose(); recomposer.cancel(); runner.join() }
    }

    @Test fun stale_generation_is_hidden_and_cannot_publish_then_retry_can_succeed() = runTest {
        val file = temporary.newFile(MapResolve.MAP_FILES.getValue("ГК" to 1)).apply { writeBytes(byteArrayOf(1)) }
        val files = mapOf(1 to file)
        val revision = mutableStateOf(StackRasterRevision.capture("ГК", files))
        val frames = BroadcastFrameClock()
        val recomposer = Recomposer(coroutineContext + frames)
        val runner = launch(frames) { recomposer.runRecomposeAndApplyChanges() }
        val composition = Composition(NoNodes(), recomposer)
        val late = CompletableDeferred<Int>()
        var starts = 0
        var visible: Int? = null
        val observed = mutableListOf<Int?>()
        try {
            composition.setContent {
                visible = produceThumbnails("ГК", listOf(1), files, revision.value) {
                    when (++starts) {
                        1 -> 1
                        2 -> withContext(NonCancellable) { late.await() }
                        3 -> null // metadata revalidation rejected this batch
                        else -> 4
                    }
                }
                observed += visible
            }
            suspend fun settle() {
                repeat(4) { Snapshot.sendApplyNotifications(); runCurrent(); frames.sendFrame(0); runCurrent() }
            }
            settle()
            assertEquals(1, visible)
            file.appendBytes(byteArrayOf(2))
            revision.value = StackRasterRevision.capture("ГК", files)
            observed.clear()
            settle()
            assertTrue("old published generation must be gated immediately", observed.all { it == null })
            assertEquals(2, starts)
            file.appendBytes(byteArrayOf(3))
            revision.value = StackRasterRevision.capture("ГК", files)
            settle()
            late.complete(2)
            settle()
            assertEquals(3, starts)
            assertNull(visible)
            assertFalse("cancelled noncooperative generation cannot publish", 2 in observed)
            revision.value = StackRasterRevision.capture("ГК", files, retry = 1)
            settle()
            assertEquals(4, starts)
            assertEquals(4, visible)
        } finally { late.complete(2); composition.dispose(); recomposer.cancel(); runner.join() }
    }

    private class NoNodes : AbstractApplier<Unit>(Unit) {
        override fun insertTopDown(index: Int, instance: Unit) = Unit
        override fun insertBottomUp(index: Int, instance: Unit) = Unit
        override fun remove(index: Int, count: Int) = Unit
        override fun move(from: Int, to: Int, count: Int) = Unit
        override fun onClear() = Unit
    }
}
