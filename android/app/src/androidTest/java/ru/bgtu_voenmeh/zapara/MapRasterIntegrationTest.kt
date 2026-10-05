package ru.bgtu_voenmeh.zapara

import android.app.Application
import android.content.Context
import android.graphics.Bitmap
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.runtime.collectAsState
import androidx.compose.ui.graphics.toPixelMap
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.ViewModelStore
import androidx.room.Room
import java.io.ByteArrayOutputStream
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.data.api.ApiRefreshCoordinator
import ru.bgtu_voenmeh.zapara.data.api.RoomTimetableStore
import ru.bgtu_voenmeh.zapara.data.api.UrlConnectionTransport
import ru.bgtu_voenmeh.zapara.data.db.ZaparaDatabase
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileDescriptor
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork
import ru.bgtu_voenmeh.zapara.ui.maps.MapFullscreen
import ru.bgtu_voenmeh.zapara.ui.maps.MapsEvent
import ru.bgtu_voenmeh.zapara.ui.maps.MapsSection
import ru.bgtu_voenmeh.zapara.ui.maps.MapsViewModel
import ru.bgtu_voenmeh.zapara.ui.theme.MotionSettings
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme

class MapRasterIntegrationTest {
    @get:Rule val compose = createAndroidComposeRule<ComponentActivity>()

    private class TestApplication(base: Context) : Application() {
        init { attachBaseContext(base) }
    }

    @Test fun embedded_same_path_length_only_refresh_repaints() = refresh(fullscreen = false)
    @Test fun fullscreen_same_path_mtime_only_refresh_repaints() = refresh(fullscreen = true)

    private fun refresh(fullscreen: Boolean) = runBlocking {
        MapTestContext().use { context ->
            val app = TestApplication(context)
            val container = withContext(Dispatchers.IO) {
                val db = Room.inMemoryDatabaseBuilder(app, ZaparaDatabase::class.java).build()
                val store = RoomTimetableStore(db)
                val work = ProfileWork()
                val repo = ScheduleRepository(db, store, work)
                repo.saveSettings(repo.settings().copy(mapsAlpha = true))
                AppContainer(app, ProfileDescriptor.guest(), db, repo, work,
                    ApiRefreshCoordinator(store, work, null, UrlConnectionTransport()))
            }
            val owner = ViewModelStore()
            try {
                val vm = withContext(Dispatchers.Main) {
                    ViewModelProvider(owner, MapsViewModel.factory(container, null))[MapsViewModel::class.java]
                }
                val initial = withTimeout(10_000) { vm.state.first { it.stackRasterRevision != null } }
                val red = raster(android.graphics.Color.RED)
                val blue = raster(android.graphics.Color.BLUE)
                val length = maxOf(red.size, blue.size) + 32
                withContext(Dispatchers.IO) {
                    initial.floorFiles.values.forEach { it.writeBytes(red.copyOf(length)) }
                }
                withContext(Dispatchers.Main) { vm.onEvent(MapsEvent.ShowRoom("101")) }
                val before = withTimeout(10_000) {
                    vm.state.first { it.stackRasterRevision != initial.stackRasterRevision }
                }
                assertEquals("ГК", before.building)
                assertEquals(before.floorFiles[before.floor], before.planFile)
                withContext(Dispatchers.Main) { vm.onEvent(MapsEvent.ToggleStack) }
                compose.setContent {
                    val state = vm.state.collectAsState().value
                    ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
                        if (fullscreen) MapFullscreen(state, vm::onEvent)
                        else MapsSection(state, vm::onEvent)
                    }
                }
                awaitRaster(red = true)
                withContext(Dispatchers.IO) {
                    before.floorFiles.values.forEach { file ->
                        val modified = file.lastModified()
                        file.writeBytes(blue.copyOf(if (fullscreen) length else length + 1))
                        assertTrue(file.setLastModified(if (fullscreen) modified + 10_000 else modified))
                        assertEquals((if (fullscreen) length else length + 1).toLong(), file.length())
                        assertEquals(if (fullscreen) modified + 10_000 else modified, file.lastModified())
                    }
                }
                withContext(Dispatchers.Main) { vm.onEvent(MapsEvent.PickFloor(before.floor)) }
                val after = withTimeout(10_000) {
                    vm.state.first { it.stackRasterRevision != before.stackRasterRevision }
                }
                assertEquals(before.floorFiles, after.floorFiles)
                assertEquals(before.building, after.building)
                assertEquals(before.floorFiles[after.floor], after.planFile)
                assertTrue(after.showStack)
                awaitRaster(red = false)
            } finally {
                // Dispose consumers before closing their owning VM/container and isolated files.
                compose.activityRule.scenario.onActivity { it.setContent {} }
                compose.waitForIdle()
                withContext(Dispatchers.Main) { owner.clear() }
                withContext(Dispatchers.IO) { container.close() }
            }
        }
    }

    private fun awaitRaster(red: Boolean) {
        compose.waitUntil(timeoutMillis = 10_000) {
            val pixels = compose.onNodeWithTag("Maps.StackView").captureToImage().toPixelMap()
            var matching = 0
            for (y in 0 until pixels.height step 4) for (x in 0 until pixels.width step 4) {
                val color = pixels[x, y]
                if (if (red) color.red > 0.8f && color.blue < 0.2f && color.green < 0.2f
                    else color.blue > 0.8f && color.red < 0.2f && color.green < 0.2f) matching++
            }
            matching > 100
        }
    }

    private fun raster(color: Int): ByteArray {
        val bitmap = Bitmap.createBitmap(64, 64, Bitmap.Config.ARGB_8888)
        return try {
            bitmap.eraseColor(color)
            ByteArrayOutputStream().use { bytes ->
                check(bitmap.compress(Bitmap.CompressFormat.JPEG, 100, bytes))
                bytes.toByteArray()
            }
        } finally { bitmap.recycle() }
    }
}
