package ru.bgtu_voenmeh.zapara.data

import android.content.Context
import android.graphics.BitmapFactory
import android.util.AtomicFile
import org.json.JSONObject
import org.json.JSONTokener
import ru.bgtu_voenmeh.zapara.BuildConfig
import ru.bgtu_voenmeh.zapara.data.campus.CampusGraph
import ru.bgtu_voenmeh.zapara.data.campus.CampusRouter
import java.io.File
import java.security.MessageDigest

// Offline maps: bundled assets -> filesDir/maps cache. Mirrors MapService caching.
class MapStore(private val context: Context) {

    companion object {
        // AtomicFile does not serialize writers, including separate MapStore instances.
        private val cacheLock = Any()
    }

    private val dir: File by lazy {
        File(context.filesDir, "maps").also { it.mkdirs() }
    }

    private fun refreshBundledIfNeeded(fileName: String, valid: (File) -> Boolean): File? {
        val prefs = context.getSharedPreferences("zapara_maps", Context.MODE_PRIVATE)
        val out = File(dir, fileName)
        val atomic = AtomicFile(out)
        try { atomic.openRead().close() } catch (_: Exception) { }
        val key = "asset-version:$fileName"
        if (valid(out) && prefs.getInt(key, 0) == BuildConfig.VERSION_CODE) return out
        var staged: File? = null
        try {
            // Validate before touching last-good; staging works on both old and new AtomicFile implementations.
            staged = File.createTempFile("map-", ".pending", dir)
            context.assets.open("maps/$fileName").use { input ->
                staged.outputStream().use { input.copyTo(it) }
            }
            check(valid(staged)) { "Invalid bundled map" }
            val stream = atomic.startWrite()
            try {
                staged.inputStream().use { it.copyTo(stream) }
                atomic.finishWrite(stream)
            } catch (error: Exception) {
                atomic.failWrite(stream)
                throw error
            }
            // Android AtomicFile can log a failed rename instead of throwing.
            check(valid(out) && digest(out).contentEquals(digest(staged)))
            prefs.edit().putInt(key, BuildConfig.VERSION_CODE).commit()
        } catch (_: Exception) {
            // Keep the per-file version old so a later request retries the failed update.
        } finally {
            staged?.delete()
        }
        return out.takeIf(valid)
    }

    private fun digest(file: File): ByteArray {
        val digest = MessageDigest.getInstance("SHA-256")
        file.inputStream().use { input ->
            val buffer = ByteArray(8192)
            var count = input.read(buffer)
            while (count >= 0) {
                digest.update(buffer, 0, count)
                count = input.read(buffer)
            }
        }
        return digest.digest()
    }

    /** Local file for a bundled map, copying from assets on first use. */
    fun mapFile(fileName: String): File? = synchronized(cacheLock) {
        if (fileName.isEmpty() || fileName == "." || fileName == ".." ||
            fileName.contains('/') || fileName.contains('\\')) return@synchronized null
        refreshBundledIfNeeded(fileName, ::validRaster)
    }

    private fun validRaster(file: File): Boolean = try {
        if (!file.isFile) false else {
            val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            BitmapFactory.decodeFile(file.absolutePath, bounds)
            bounds.outWidth > 0 && bounds.outHeight > 0
        }
    } catch (_: Exception) { false }

    private fun json(file: File): JSONObject {
        val tokens = JSONTokener(file.readText(Charsets.UTF_8))
        val value = tokens.nextValue() as? JSONObject ?: error("Expected JSON object")
        check(tokens.nextClean() == '\u0000') { "Trailing JSON content" }
        return value
    }

    fun cacheStatus(): Pair<Int, Int> = synchronized(cacheLock) {
        val cached = MapResolve.MAP_FILES.values.count { f ->
            val local = File(dir, f)
            validRaster(local)
        }
        cached to MapResolve.MAP_FILES.size
    }

    @Volatile
    private var coordsCache: Map<String, Map<String, CoordsRect>>? = null

    /** "ГК 3" -> roomKey -> rect. Reads assets copy first, prefers filesDir override. */
    fun coords(): Map<String, Map<String, CoordsRect>> = synchronized(cacheLock) {
        coordsCache?.let { return@synchronized it }
        val parsed = mutableMapOf<String, MutableMap<String, CoordsRect>>()
        try {
            val local = refreshBundledIfNeeded("coords.json") { file ->
                try { json(file); true } catch (_: Exception) { false }
            } ?: File(dir, "coords.json")
            if (!local.exists()) return@synchronized emptyMap()
            val json = json(local)
            val maps = json.optJSONObject("maps") ?: JSONObject()
            for (key in maps.keys()) {
                val inner = maps.optJSONObject(key) ?: continue
                val rooms = mutableMapOf<String, CoordsRect>()
                for (room in inner.keys()) {
                    val r = inner.optJSONObject(room) ?: continue
                    rooms[room.lowercase()] = CoordsRect(
                        r.optDouble("x"), r.optDouble("y"),
                        r.optDouble("w"), r.optDouble("h")
                    )
                }
                parsed[key] = rooms
            }
        } catch (_: Exception) {
            return@synchronized coordsCache ?: emptyMap()
        }
        parsed.also { coordsCache = it }
    }

    fun findCoords(building: String, floor: Int, roomRaw: String?): CoordsRect? =
        MapResolve.findCoords(coords(), building, floor, roomRaw)

    fun campusGraph(): CampusGraph {
        return try {
            val json = context.assets.open("maps/campus-graph.json").use { input ->
                input.readBytes().toString(Charsets.UTF_8)
            }
            CampusGraph.load(json)
        } catch (_: Exception) {
            CampusGraph.empty
        }
    }

    fun readLastEntrance(): String? {
        val file = File(dir, "last-entrance.txt")
        if (!file.exists()) return null
        return try {
            file.readText(Charsets.UTF_8).trim().ifEmpty { null }
        } catch (_: Exception) {
            null
        }
    }

    fun saveLastEntrance(id: String) {
        try {
            File(dir, "last-entrance.txt").writeText(id, Charsets.UTF_8)
        } catch (_: Exception) {
        }
    }

    fun rememberEntrance(graph: CampusGraph, id: String): String? {
        if (CampusRouter.resolveEntrance(graph, id) == null) return null
        saveLastEntrance(id)
        return id
    }
}
