package ru.bgtu_voenmeh.zapara

import android.content.Context
import android.content.ContextWrapper
import android.content.SharedPreferences
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import ru.bgtu_voenmeh.zapara.data.MapResolve
import ru.bgtu_voenmeh.zapara.data.MapStore
import java.io.File
import java.util.UUID

/** Assets are real; writes use UUID-named test storage/preferences, never a profile. */
internal class MapTestContext : ContextWrapper(InstrumentationRegistry.getInstrumentation().targetContext), AutoCloseable {
    private val owner = InstrumentationRegistry.getInstrumentation().targetContext
    private val prefix = "task8-${UUID.randomUUID()}"
    private val root = File(owner.cacheDir, prefix).also { check(it.mkdirs()) }
    private val preferences = mutableSetOf<String>()
    override fun getFilesDir(): File = root
    override fun getSharedPreferences(name: String, mode: Int): SharedPreferences {
        val isolated = "$prefix-$name"
        preferences.add(isolated)
        return owner.getSharedPreferences(isolated, mode)
    }
    fun map(name: String): File = File(File(root, "maps").also { it.mkdirs() }, name)
    fun raster(name: String = MapResolve.MAP_FILES.values.first()): File = map(name).also { file ->
        assets.open("maps/${MapResolve.MAP_FILES.values.first()}").use { input ->
            file.outputStream().use { input.copyTo(it) }
        }
    }
    override fun close() {
        preferences.forEach { check(owner.deleteSharedPreferences(it)) }
        check(root.deleteRecursively())
    }
}

@RunWith(AndroidJUnit4::class)
class MapCacheCompatibilityTest {
    @Test fun invalid_bundled_raster_does_not_replace_last_good() = MapTestContext().use { context ->
        // A real bundled JSON asset cannot pass raster bounds validation.
        val good = context.raster("coords.json")
        val before = good.readBytes()
        val store = MapStore(context)
        assertEquals(good, store.mapFile("coords.json"))
        assertArrayEquals(before, good.readBytes())
        assertFalse(context.getSharedPreferences("zapara_maps", 0).contains("asset-version:coords.json"))
    }

    @Test fun atomic_start_failure_preserves_file_and_retries_after_obstruction_removed() = MapTestContext().use { context ->
        val name = MapResolve.MAP_FILES.values.first()
        val good = context.raster(name)
        val original = good.readBytes()
        val obstruction = context.map("$name.new").apply { check(mkdir()) }
        File(obstruction, "keep").writeText("owned obstruction")
        val store = MapStore(context)
        assertEquals(good, store.mapFile(name))
        assertArrayEquals(original, good.readBytes())
        val prefs = context.getSharedPreferences("zapara_maps", 0)
        assertFalse(prefs.contains("asset-version:$name"))
        check(obstruction.deleteRecursively())
        assertEquals(good, store.mapFile(name))
        assertEquals(BuildConfig.VERSION_CODE, prefs.getInt("asset-version:$name", 0))
        assertTrue(context.map(name).parentFile!!.listFiles()!!.none { it.name.endsWith(".pending") })
    }

    @Test fun coords_follow_app_version_like_rasters() = MapTestContext().use { context ->
        val local = context.map("coords.json")
        local.writeText("""{"maps":{"ГК 1":{"1":{"x":0,"y":0,"w":1,"h":1}}}}""")
        context.getSharedPreferences("zapara_maps", 0).edit().putInt("asset-version:coords.json", 1).commit()
        val store = MapStore(context)
        val coords = store.coords()
        assertTrue(coords.isNotEmpty())
        assertEquals(BuildConfig.VERSION_CODE,
            context.getSharedPreferences("zapara_maps", 0).getInt("asset-version:coords.json", 0))
        val bundled = context.assets.open("maps/coords.json").bufferedReader().readText()
        assertEquals(org.json.JSONObject(bundled).getJSONObject("maps").length(),
            org.json.JSONObject(local.readText()).getJSONObject("maps").length())
    }

    @Test fun coords_install_is_valid_and_cached_override_survives_later_damage() = MapTestContext().use { context ->
        val store = MapStore(context)
        val coords = store.coords()
        assertTrue(coords.isNotEmpty())
        val local = context.map("coords.json")
        org.json.JSONObject(local.readText())
        assertEquals(BuildConfig.VERSION_CODE,
            context.getSharedPreferences("zapara_maps", 0).getInt("asset-version:coords.json", 0))
        local.writeText("broken local override")
        assertEquals(coords, store.coords())
        assertTrue(MapStore(context).coords().isEmpty())
        assertEquals("broken local override", local.readText())
    }

    @Test fun invalid_missing_and_path_escape_requests_do_not_create_success_markers() = MapTestContext().use { context ->
        val store = MapStore(context)
        for (name in listOf("missing.jpg", "empty.jpg", "broken.jpg", "../escape.jpg", "", "..")) {
            if (name == "empty.jpg") context.map(name).writeBytes(byteArrayOf())
            if (name == "broken.jpg") context.map(name).writeBytes(ByteArray(2048) { 7 })
            assertNull(name, store.mapFile(name))
            assertFalse(context.getSharedPreferences("zapara_maps", 0).contains("asset-version:$name"))
        }
        assertEquals(0 to 9, store.cacheStatus())
    }

    @Test fun failed_asset_update_keeps_last_good_and_neighbors() = MapTestContext().use { context ->
        val name = "cache-test-missing-asset.jpg"
        val good = context.raster(name)
        val original = good.readBytes()
        val neighbors = listOf("coords.json", "last-entrance.txt", "neighbor.bin").associateWith {
            val content = if (it == "coords.json") "{\"maps\":{}}" else "keep-$it"
            context.map(it).apply { writeText(content) }.readBytes()
        }
        val prefs = context.getSharedPreferences("zapara_maps", Context.MODE_PRIVATE)
        assertTrue(prefs.edit().putInt("assets", BuildConfig.VERSION_CODE - 1).commit())
        assertFalse(context.assets.list("maps")!!.contains(name))
        assertEquals(good, MapStore(context).mapFile(name))
        assertArrayEquals(original, good.readBytes())
        neighbors.forEach { (file, bytes) -> assertArrayEquals(file, bytes, context.map(file).readBytes()) }
        assertFalse(prefs.contains("asset-version:$name"))
        assertEquals(BuildConfig.VERSION_CODE - 1, prefs.getInt("assets", 0))
    }

    @Test fun successful_update_marks_only_requested_file_and_preserves_coords_override() = MapTestContext().use { context ->
        val override = context.map("coords.json").apply {
            writeText("{\"maps\":{\"ГК 1\":{\"test\":{\"x\":0.1,\"y\":0.2,\"w\":0.3,\"h\":0.4}}}}")
        }.readBytes()
        val prefs = context.getSharedPreferences("zapara_maps", Context.MODE_PRIVATE)
        assertTrue(prefs.edit().putInt("assets", BuildConfig.VERSION_CODE).commit())
        val store = MapStore(context)
        val name = MapResolve.MAP_FILES.values.first()
        assertNotNull(store.mapFile(name))
        assertEquals(BuildConfig.VERSION_CODE, prefs.getInt("asset-version:$name", 0))
        assertFalse(prefs.contains("asset-version:${MapResolve.MAP_FILES.values.last()}"))
        assertNotNull(store.coords()["ГК 1"]?.get("test"))
        assertArrayEquals(override, context.map("coords.json").readBytes())
    }
}
