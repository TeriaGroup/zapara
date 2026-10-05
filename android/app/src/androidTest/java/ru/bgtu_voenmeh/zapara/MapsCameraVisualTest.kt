package ru.bgtu_voenmeh.zapara

import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Surface
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createEmptyComposeRule
import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.maps.MapsEvent
import ru.bgtu_voenmeh.zapara.ui.maps.MapsSection
import ru.bgtu_voenmeh.zapara.ui.maps.MapsUiState
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ShellChrome
import ru.bgtu_voenmeh.zapara.ui.theme.MotionSettings
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme

class MapsCameraVisualTest {
    @get:Rule val rule = createEmptyComposeRule()

    @Test fun fullscreen_keeps_zoom_and_camera_on_real_plan() = withPlan { file ->
        var state by mutableStateOf(MapsUiState(loaded = true, planFile = file,
            zoom = 1.25f, panX = 0.08f))
        OwnedTestHost.launch().use { host ->
            show(host, { state }) { event ->
                state = when (event) {
                    is MapsEvent.Fullscreen -> state.copy(fullscreen = event.on)
                    is MapsEvent.Transform -> state.copy(zoom = event.zoom)
                    is MapsEvent.Pan -> state.copy(panX = event.x, panY = event.y)
                    else -> state
                }
            }
            rule.onNodeWithTag("Maps.Fullscreen").performScrollTo().performClick()
            rule.onNodeWithTag("Maps.Close").assertIsDisplayed()
            rule.runOnIdle {
                assertEquals(1.25f, state.zoom, 0.001f)
                assertEquals(0.08f, state.panX, 0.0001f)
            }
            rule.onNodeWithTag("Maps.Close").performClick()
            rule.runOnIdle {
                assertEquals(1.25f, state.zoom, 0.001f)
                assertEquals(0.08f, state.panX, 0.0001f)
            }
        }
    }

    @Test fun map_shows_loading_until_bitmap_is_ready() = withPlan { file ->
        var state by mutableStateOf(MapsUiState(loaded = false, planFile = null))
        OwnedTestHost.launch().use { host ->
            show(host, { state }) { event ->
                if (event is MapsEvent.Transform) state = state.copy(zoom = event.zoom)
            }
            rule.onNodeWithTag("Maps.PlanLoading").assertIsDisplayed()
            rule.runOnIdle { state = state.copy(loaded = true, planFile = file) }
            rule.waitUntil(10_000) {
                rule.onAllNodesWithTag("Maps.PlanLoading").fetchSemanticsNodes().isEmpty()
            }
            rule.onNodeWithTag("Maps.Plan").assertIsDisplayed()
            rule.onNodeWithTag("Maps.MapUnavailable").assertDoesNotExist()
        }
    }

    private fun show(host: OwnedTestHost, state: () -> MapsUiState, onEvent: (MapsEvent) -> Unit) {
        host.scenario.onActivity { activity -> activity.setContent {
            ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
                CompositionLocalProvider(LocalShellChrome provides ShellChrome("Тестовая группа", false, true) {}) {
                    Surface(Modifier.fillMaxSize(), color = Zapara.colors.canvas) {
                        MapsSection(state(), onEvent)
                    }
                }
            }
        } }
        rule.waitForIdle()
        host.awaitForeground()
    }

    private fun withPlan(block: (File) -> Unit) {
        val context = androidx.test.platform.app.InstrumentationRegistry.getInstrumentation().targetContext
        val file = File(context.cacheDir, "qa-map-camera-${System.nanoTime()}.jpg")
        try {
            context.assets.open("maps/karta-glavnyj-korpus-1-etazh-2022.jpg").use { input ->
                file.outputStream().use { output -> input.copyTo(output) }
            }
            check(file.length() > 0)
            block(file)
        } finally {
            file.delete()
        }
    }
}
