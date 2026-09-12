package ru.bgtu_voenmeh.zapara.ui.maps

import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import java.io.File
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive

private class ProducedThumbnail<T>(val generation: Any, val value: T)

/** Shared production boundary: equal keys keep work; new keys hide old state before effects run. */
@Composable
internal fun <T> produceThumbnails(
    building: String,
    floors: List<Int>,
    files: Map<Int, File>,
    revision: StackRasterRevision?,
    load: suspend () -> T?
): T? {
    val generation = remember(building, floors, files, revision) { Any() }
    val produced by produceState<ProducedThumbnail<T>?>(null, generation) {
        value = null
        val loaded = load()
        currentCoroutineContext().ensureActive()
        if (loaded != null) value = ProducedThumbnail(generation, loaded)
    }
    return produced?.takeIf { it.generation === generation }?.value
}
