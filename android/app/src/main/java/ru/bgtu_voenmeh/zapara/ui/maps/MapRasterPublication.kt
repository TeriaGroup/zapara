package ru.bgtu_voenmeh.zapara.ui.maps

import java.io.File
import java.util.Collections
import kotlin.coroutines.AbstractCoroutineContextElement
import kotlin.coroutines.CoroutineContext
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/** Files and metadata are captured on IO and handed to state as one immutable value. */
internal class MapRasterPublication private constructor(
    val building: String,
    val files: Map<Int, File>,
    val revision: StackRasterRevision
) {
    fun applyTo(state: MapsUiState): MapsUiState = state.copy(
        building = building, floors = MapsComposer.floors(building),
        floorFiles = files, stackRasterRevision = revision
    )

    companion object {
        suspend fun capture(building: String, ioDispatcher: CoroutineDispatcher = Dispatchers.IO,
            retry: Long = 0, load: (String) -> Map<Int, File>): MapRasterPublication =
            withContext(ioDispatcher) {
                val shown = shownBuilding(building)
                val files = Collections.unmodifiableMap(LinkedHashMap(load(shown)))
                MapRasterPublication(shown, files, StackRasterRevision.capture(shown, files, retry, ioDispatcher))
            }
    }
}

/** Owned by the VM's Main scope. A new request invalidates work before it is scheduled. */
internal class MapLoadRequests(private val scope: CoroutineScope) {
    private class Request(val generation: Long) : AbstractCoroutineContextElement(Key) {
        companion object Key : CoroutineContext.Key<Request>
    }

    private var generation = 0L
    private var job: Job? = null
    private var explicit = false

    fun refresh(mode: MapMode, block: suspend () -> Unit): Job? {
        if (mode == MapMode.Manual || mode == MapMode.Lesson) return null
        // Published mode may still be NextLesson while a manual/room request awaits IO.
        if (explicit && job?.isActive == true) return null
        return start(explicit = false, block)
    }

    fun launch(block: suspend () -> Unit): Job = start(explicit = true, block)

    private fun start(explicit: Boolean, block: suspend () -> Unit): Job {
        this.explicit = explicit
        val request = Request(++generation)
        job?.cancel()
        return scope.launch(request) { block() }.also { job = it }
    }

    suspend fun ensureCurrent() {
        val context = currentCoroutineContext()
        context.ensureActive()
        if (context[Request]?.generation != generation) throw CancellationException("Superseded map load")
    }
}
