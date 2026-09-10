package ru.bgtu_voenmeh.zapara.data

import android.content.Context
import org.json.JSONObject
import java.io.File

// Offline maps: bundled assets -> filesDir/maps cache. Mirrors MapService caching.
class MapStore(private val context: Context) {

    private val dir: File by lazy {
        File(context.filesDir, "maps").also { it.mkdirs() }
    }

    /** Local file for a bundled map, copying from assets on first use. */
    fun mapFile(fileName: String): File? {
        if (fileName.isEmpty()) return null
        val out = File(dir, fileName)
        if (out.exists() && out.length() > 1000) return out
        return try {
            context.assets.open("maps/$fileName").use { input ->
                out.outputStream().use { input.copyTo(it) }
            }
            if (out.length() > 1000) out else null
        } catch (_: Exception) {
            null
        }
    }

    fun cacheStatus(): Pair<Int, Int> {
        val cached = MapResolve.MAP_FILES.values.count { f ->
            val local = File(dir, f)
            local.exists() && local.length() > 1000
        }
        return cached to MapResolve.MAP_FILES.size
    }

    @Volatile
    private var coordsCache: Map<String, Map<String, CoordsRect>>? = null

    /** "ГК 3" -> roomKey -> rect. Reads assets copy first, prefers filesDir override. */
    fun coords(): Map<String, Map<String, CoordsRect>> {
        coordsCache?.let { return it }
        val parsed = mutableMapOf<String, MutableMap<String, CoordsRect>>()
        try {
            val local = File(dir, "coords.json")
            context.assets.open("maps/coords.json").use { input ->
                local.outputStream().use { input.copyTo(it) }
            }
            val json = JSONObject(local.readText(Charsets.UTF_8))
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
        }
        return parsed.also { coordsCache = it }
    }

    fun findCoords(building: String, floor: Int, roomRaw: String?): CoordsRect? =
        MapResolve.findCoords(coords(), building, floor, roomRaw)
}
