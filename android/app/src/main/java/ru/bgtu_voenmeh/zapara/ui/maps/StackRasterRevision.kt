package ru.bgtu_voenmeh.zapara.ui.maps

import java.io.File
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.data.MapResolve

/** Point-in-time load identity. Capture after each explicit catalog refresh, never in composition. */
class StackRasterRevision private constructor(
    internal val building: String,
    private val keys: List<ThumbnailKey>,
    private val retry: Long
) {
    internal fun keysFor(building: String, floors: List<Int>): List<ThumbnailKey> =
        if (shownBuilding(building) == this.building) keys.filter { it.floor.floor in floors } else emptyList()

    override fun equals(other: Any?): Boolean = other is StackRasterRevision &&
        building == other.building && keys == other.keys && retry == other.retry

    override fun hashCode(): Int = 31 * (31 * building.hashCode() + keys.hashCode()) + retry.hashCode()

    companion object {
        /** retry changes only for an explicit retry of unchanged bytes, not for ordinary refreshes. */
        suspend fun capture(building: String, files: Map<Int, File>, retry: Long = 0,
            ioDispatcher: CoroutineDispatcher = Dispatchers.IO): StackRasterRevision =
            withContext(ioDispatcher) {
                val shown = shownBuilding(building)
                val keys = files.entries.sortedBy { it.key }.mapNotNull { (floor, file) ->
                    val expected = MapResolve.MAP_FILES[shown to floor]
                    if (expected == null || file.name != expected || !file.isFile) null
                    else ThumbnailKey(FloorKey(shown, floor), file.absolutePath, file.length(), file.lastModified())
                }.take(5)
                StackRasterRevision(shown, keys, retry)
            }
    }
}
