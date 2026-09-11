package ru.bgtu_voenmeh.zapara.ui.maps

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import java.io.File
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.data.MapResolve
import ru.bgtu_voenmeh.zapara.data.MapStore

data class FloorRaster(val file: File, val size: RasterSize)

class FloorRasterLoader(private val mapStore: MapStore, private val io: CoroutineDispatcher = Dispatchers.IO) {
    suspend fun load(keys: Set<FloorKey>): Map<FloorKey, FloorRaster> = withContext(io) {
        val result = LinkedHashMap<FloorKey, FloorRaster>()
        for (requested in keys) {
            currentCoroutineContext().ensureActive()
            val key = requested.copy(building = shownBuilding(requested.building))
            if (key in result) continue
            val name = MapResolve.MAP_FILES[key.building to key.floor] ?: continue
            val file = mapStore.mapFile(name) ?: continue
            val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            BitmapFactory.decodeFile(file.absolutePath, bounds)
            if (bounds.outWidth > 0 && bounds.outHeight > 0) {
                result[key] = FloorRaster(file, RasterSize(bounds.outWidth, bounds.outHeight))
            }
        }
        result
    }
}

internal fun shownBuilding(building: String): String = if (building == "ВЦ") "ГК" else building

internal data class ThumbnailKey(val floor: FloorKey, val path: String, val length: Long, val modified: Long) {
    fun unchanged(): Boolean {
        val file = File(path)
        return file.isFile && file.length() == length && file.lastModified() == modified
    }
}

internal class ThumbnailBatch(private val request: Any, private val images: Map<ThumbnailKey, Bitmap>) {
    fun forRequest(current: Any): Map<Int, Bitmap> =
        if (current === request) images.entries.associate { it.key.floor.floor to it.value } else emptyMap()
}

/** One non-overlapping decoder even while cancellation waits for a native decode to return. */
internal class StackThumbnailLoader {
    private val mutex = Mutex()

    suspend fun load(
        building: String,
        files: Map<Int, File>,
        revision: StackRasterRevision? = null
    ): Map<ThumbnailKey, Bitmap> =
        withContext(Dispatchers.IO) {
            mutex.withLock {
                val snapshot = revision ?: StackRasterRevision.capture(building, files)
                val keys = snapshot.keysFor(building, files.keys.toList())
                if (keys.any { !it.unchanged() || files[it.floor.floor]?.absolutePath != it.path }) {
                    return@withLock emptyMap()
                }
                val decoded = LinkedHashMap<ThumbnailKey, Bitmap>()
                var returned = false
                try {
                    for (key in keys) {
                        currentCoroutineContext().ensureActive()
                        val bitmap = CampusStackProjector.decodeThumb(File(key.path)) ?: continue
                        decoded[key] = bitmap
                        currentCoroutineContext().ensureActive()
                    }
                    if (keys.any { !it.unchanged() }) return@withLock emptyMap()
                    returned = true
                    decoded
                } finally {
                    // Unpublished/cancelled work only. Published images are GC-owned by Compose.
                    if (!returned) decoded.values.forEach { it.recycle() }
                }
            }
        }
}
