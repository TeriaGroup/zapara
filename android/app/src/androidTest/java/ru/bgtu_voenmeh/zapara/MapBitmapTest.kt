package ru.bgtu_voenmeh.zapara

import androidx.test.ext.junit.runners.AndroidJUnit4
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.async
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import ru.bgtu_voenmeh.zapara.ui.maps.CampusStackProjector
import ru.bgtu_voenmeh.zapara.ui.maps.FloorKey
import ru.bgtu_voenmeh.zapara.ui.maps.FloorRasterLoader
import ru.bgtu_voenmeh.zapara.ui.maps.StackThumbnailLoader
import ru.bgtu_voenmeh.zapara.ui.maps.StackRasterRevision
import ru.bgtu_voenmeh.zapara.ui.maps.ThumbnailBatch
import ru.bgtu_voenmeh.zapara.data.MapResolve
import ru.bgtu_voenmeh.zapara.data.MapStore

@RunWith(AndroidJUnit4::class)
class MapBitmapTest {
    @Test fun captured_revision_rejects_replaced_bytes_and_fresh_capture_retries() = runBlocking {
        MapTestContext().use { context ->
            val file = context.raster()
            val files = mapOf(1 to file)
            val loader = StackThumbnailLoader()
            val before = StackRasterRevision.capture("ГК", files)
            file.appendBytes(byteArrayOf(0))
            assertTrue(loader.load("ГК", files, before).isEmpty())
            val after = StackRasterRevision.capture("ГК", files)
            assertNotEquals(before, after)
            val images = loader.load("ГК", files, after)
            try {
                assertEquals(1, images.size)
                assertTrue(images.keys.single().unchanged())
            } finally { images.values.forEach { it.recycle() } }
        }
    }

    @Test fun publication_rejects_old_generation_without_recycling_drawn_bitmap() = runBlocking {
        MapTestContext().use { context ->
            val images = StackThumbnailLoader().load("ГК", mapOf(1 to context.raster()))
            val oldRequest = Any()
            val batch = ThumbnailBatch(oldRequest, images)
            try {
                assertEquals(setOf(1), batch.forRequest(oldRequest).keys)
                assertTrue(batch.forRequest(Any()).isEmpty())
                assertFalse(images.values.single().isRecycled)
            } finally { images.values.forEach { it.recycle() } }
        }
    }

    @Test fun catalog_preserves_all_nine_original_bounds_and_normalizes_vc() = runBlocking {
        MapTestContext().use { context ->
            val keys = MapResolve.MAP_FILES.keys.map { FloorKey(it.first, it.second) }.toSet()
            val catalog = FloorRasterLoader(MapStore(context)).load(keys + FloorKey("ВЦ", 1) + FloorKey("missing", 9))
            assertEquals(keys, catalog.keys)
            assertEquals(9, catalog.size)
            for ((key, raster) in catalog) {
                assertEquals(MapResolve.MAP_FILES[key.building to key.floor], raster.file.name)
                val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
                BitmapFactory.decodeFile(raster.file.absolutePath, bounds)
                assertEquals(bounds.outWidth, raster.size.width)
                assertEquals(bounds.outHeight, raster.size.height)
                val bitmap = CampusStackProjector.decodeThumb(raster.file)!!
                try {
                    assertTrue(maxOf(bitmap.width, bitmap.height) <= 256)
                    assertTrue(bitmap.byteCount <= 256 * 256 * 4)
                    val expectedHeight = bitmap.width.toDouble() * raster.size.height / raster.size.width
                    assertEquals(expectedHeight, bitmap.height.toDouble(), 2.0)
                } finally { bitmap.recycle() }
            }
            assertEquals(9 to 9, MapStore(context).cacheStatus())
        }
    }

    @Test fun small_raster_is_not_upscaled_and_custom_edge_is_bounded() = MapTestContext().use { context ->
        val file = context.map("small.png")
        val source = Bitmap.createBitmap(41, 19, Bitmap.Config.ARGB_8888)
        try { file.outputStream().use { assertTrue(source.compress(Bitmap.CompressFormat.PNG, 100, it)) } }
        finally { source.recycle() }
        val small = CampusStackProjector.decodeThumb(file)!!
        try { assertEquals(41, small.width); assertEquals(19, small.height) } finally { small.recycle() }
        val custom = CampusStackProjector.decodeThumb(file, 17)!!
        try { assertEquals(17, custom.width); assertEquals(7, custom.height) } finally { custom.recycle() }
        assertNull(CampusStackProjector.decodeThumb(file, 0))
        assertNull(CampusStackProjector.decodeThumb(file, 257))
        assertNull(CampusStackProjector.decodeThumb(file, Int.MAX_VALUE))
    }

    @Test fun bad_files_and_bounds_are_rejected() = MapTestContext().use { context ->
        val absent = context.map("missing.jpg")
        val empty = context.map("empty.jpg").apply { writeBytes(byteArrayOf()) }
        val bad = context.map("bad.jpg").apply { writeBytes(ByteArray(4096) { 3 }) }
        val directory = context.map("directory").apply { mkdir() }
        listOf(absent, empty, bad, directory).forEach { assertNull(CampusStackProjector.decodeThumb(it)) }
        assertNull(CampusStackProjector.decodeThumb(context.raster(), -1))
    }

    @Test fun repeated_building_loads_keep_only_current_floors_and_budget() = runBlocking {
        MapTestContext().use { context ->
            val store = MapStore(context)
            val loader = StackThumbnailLoader()
            repeat(8) { turn ->
                val building = if (turn % 2 == 0) "ГК" else "УЛК"
                val files = MapResolve.MAP_FILES.filterKeys { it.first == building }
                    .map { it.key.second to store.mapFile(it.value)!! }.toMap()
                val images = loader.load(building, files + (99 to files.values.first()))
                try {
                    assertEquals(if (building == "ГК") 4 else 5, images.size)
                    assertTrue(images.keys.all { it.floor.building == building && it.unchanged() })
                    assertTrue(images.values.sumOf { it.byteCount } <= 1_310_720)
                    assertTrue(images.values.all { !it.isRecycled && maxOf(it.width, it.height) <= 256 })
                } finally { images.values.forEach { it.recycle() } }
            }
            val name = MapResolve.MAP_FILES["ГК" to 1]!!
            val file = store.mapFile(name)!!
            val images = loader.load("ВЦ", mapOf(1 to file))
            try {
                assertEquals(FloorKey("ГК", 1), images.keys.single().floor)
                file.appendBytes(byteArrayOf(0))
                assertFalse(images.keys.single().unchanged())
                assertFalse(images.values.single().isRecycled)
            } finally { images.values.forEach { it.recycle() } }
            assertTrue(loader.load("УЛК", mapOf(1 to file)).isEmpty())
        }
    }

    @Test fun cancelled_request_cannot_publish_and_next_request_loads() = runBlocking {
        MapTestContext().use { context ->
            val file = context.raster()
            val loader = StackThumbnailLoader()
            val cancelled = async(start = CoroutineStart.UNDISPATCHED) { loader.load("ГК", mapOf(1 to file)) }
            cancelled.cancelAndJoin()
            assertTrue(cancelled.isCancelled)
            val current = loader.load("ГК", mapOf(1 to file))
            try { assertEquals(1, current.size) } finally { current.values.forEach { it.recycle() } }
        }
    }

    @Test fun real_raster_thumbnail_has_strict_edge_limit() = MapTestContext().use { context ->
        val bitmap = CampusStackProjector.decodeThumb(context.raster())
        assertNotNull(bitmap)
        bitmap!!
        try {
            assertTrue("${bitmap.width}x${bitmap.height}", maxOf(bitmap.width, bitmap.height) <= 256)
            assertTrue(bitmap.byteCount <= 256 * 256 * 4)
        } finally { bitmap.recycle() }
    }
}
