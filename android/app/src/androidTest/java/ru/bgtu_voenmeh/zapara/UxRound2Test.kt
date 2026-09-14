package ru.bgtu_voenmeh.zapara

import android.provider.Settings
import android.view.WindowInsets
import androidx.activity.ComponentActivity
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.*
import androidx.compose.material3.Scaffold
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.unit.dp
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.UiDevice
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.campus.*
import ru.bgtu_voenmeh.zapara.ui.homework.*
import ru.bgtu_voenmeh.zapara.ui.maps.*
import ru.bgtu_voenmeh.zapara.ui.shell.*
import ru.bgtu_voenmeh.zapara.ui.theme.*
import java.io.File

/** Actual shell primitives and platform Dialog, native scale/insets, no user-data writes. */
class UxRound2Test {
    @get:Rule val rule = createAndroidComposeRule<ComponentActivity>()
    private val device = UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
    private val route = Route(120, listOf(
        Leg("walk", "ГК", 1, null, null, listOf(GraphPoint(.2, .3), GraphPoint(.6, .3))),
        Leg("stair_up", "ГК", 1, "ГК", 2, listOf(GraphPoint(.6, .3), GraphPoint(.6, .3))),
        Leg("stair_up", "ГК", 2, "ГК", 3, listOf(GraphPoint(.6, .3), GraphPoint(.6, .3))),
        Leg("walk", "ГК", 3, null, null, listOf(GraphPoint(.6, .3), GraphPoint(.8, .3))),
        Leg("stair_up", "ГК", 3, "ГК", 4, listOf(GraphPoint(.8, .3), GraphPoint(.8, .3))),
        Leg("walk", "ГК", 4, null, null, listOf(GraphPoint(.8, .3), GraphPoint(.9, .3)))))
    private val presentation = RoutePresentationBuilder.build(route, null, null,
        (1..4).associate { FloorKey("ГК", it) to RasterSize(100, 100) })
    private var state by mutableStateOf(MapsUiState(loaded = true, route = route,
        presentation = presentation, activeStepId = presentation.steps.first().id,
        fromLabel = "Вход ГК", toLabel = "493 · ГК"))

    private fun event(value: MapsEvent) {
        state = when (value) {
            MapsEvent.OpenRouteSteps -> state.copy(stepsOpen = true)
            MapsEvent.CloseRouteSteps -> state.copy(stepsOpen = false)
            is MapsEvent.SelectRouteStep -> RouteNavigation.select(state, value.id)
            MapsEvent.NextRouteStep -> RouteNavigation.move(state, 1)
            MapsEvent.PreviousRouteStep -> RouteNavigation.move(state, -1)
            else -> state
        }
    }

    private fun nativeScale(expected: Float) {
        assertEquals("Runner must set SYSTEM font_scale before launch", expected,
            Settings.System.getFloat(rule.activity.contentResolver, Settings.System.FONT_SCALE), .01f)
        assertEquals(expected, rule.activity.resources.configuration.fontScale, .01f)
    }

    private fun maps() {
        rule.runOnUiThread { rule.activity.enableEdgeToEdge() }
        rule.setContent { ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
            CompositionLocalProvider(LocalShellChrome provides ShellChrome("А863С", false, true) {}) {
                Scaffold(containerColor = Zapara.colors.canvas,
                    bottomBar = { ZBottomBar(Section.Maps, false, 0, false, {}, {}) }) { padding ->
                    Box(Modifier.padding(padding).fillMaxSize()) { MapsSection(state, ::event) }
                }
            }
        } }
        rule.waitForIdle()
    }

    private fun capture(name: String) {
        rule.waitForIdle()
        Thread.sleep(700)
        val dir = File(rule.activity.getExternalFilesDir(null), "ux-round2").apply { mkdirs() }
        assertTrue(device.takeScreenshot(File(dir, "$name.png")))
        File(dir, "$name.windows.txt").writeText(device.executeShellCommand("dumpsys window windows"))
    }

    private fun full(tag: String, sheet: Boolean = false): SemanticsNodeInteraction {
        val node = if (sheet) rule.onNode(hasTestTag(tag) and
            hasAnyAncestor(hasTestTag("Maps.StepsSheet")), true) else rule.onNodeWithTag(tag)
        node.assertIsDisplayed().assertHeightIsAtLeast(48.dp).assertWidthIsAtLeast(48.dp)
        val visible = node.fetchSemanticsNode().boundsInRoot
        val uncut = node.getUnclippedBoundsInRoot()
        assertEquals("$tag clipped", (uncut.bottom - uncut.top).value * rule.density.density, visible.height, 1f)
        return node
    }

    @Test fun initial_six_step_sheet_keeps_building_row_without_manual_scroll() {
        nativeScale(1f)
        assertEquals(6, presentation.steps.size)
        maps()
        rule.onNodeWithTag("Maps.AllSteps").performScrollTo().performClick()
        capture("sheet-initial")
        full("Maps.Building.0", true)
        full("Maps.Building.1", true)
        rule.onNodeWithTag("Maps.Step.${presentation.steps.first().id}").assertIsSelected()
    }

    @Test fun actual_short_shell_system200_retains_canvas_and_controls() {
        nativeScale(2f)
        rule.waitUntil(8_000) { device.displayWidth > device.displayHeight }
        val density = rule.activity.resources.displayMetrics.density
        assertEquals(808f, device.displayWidth / density, 1f)
        assertEquals(360f, device.displayHeight / density, 1f)
        maps()
        capture("shell-short")
        val map = rule.onNodeWithTag("Maps.MapUnavailable").assertIsDisplayed().fetchSemanticsNode().boundsInRoot
        assertTrue("Actual canvas/fallback height ${map.height / rule.density.density}dp <160dp",
            map.height >= 160 * rule.density.density - 1)
        listOf("Nav.Schedule", "Nav.Maps", "Nav.Homework", "Nav.Sections").forEach { full(it) }
        rule.onNodeWithTag("Maps.Fullscreen").performScrollTo()
        full("Maps.Fullscreen")
        rule.onNodeWithTag("Maps.AllSteps").performScrollTo()
        full("Maps.AllSteps").performClick()
        rule.onNodeWithTag("Maps.Step.${presentation.steps.first().id}").assertIsSelected()
        assertSame(route, state.route)
    }

    @OptIn(androidx.compose.ui.ExperimentalComposeUiApi::class)
    @Test fun homework_system200_ime_surface_stays_below_statusbar() {
        nativeScale(2f)
        var editor by mutableStateOf(HomeworkEditorState(null, "матан", "Высшая математика", "Прочитать главу", 1, false) { _, _ -> null })
        var open by mutableStateOf(true)
        rule.runOnUiThread { rule.activity.enableEdgeToEdge() }
        rule.setContent { ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
            if (open) HomeworkEditorSheet(editor, { editor = editor.withText(it) }, {}, {},
                { fail("Must never save") }, { open = false })
        } }
        rule.waitUntil(8_000) {
            val node = rule.onAllNodesWithTag("Editor.Text").fetchSemanticsNodes().singleOrNull()
            rule.runOnIdle { (node?.root as? androidx.compose.ui.platform.ViewRootForTest)?.view?.hasWindowFocus() == true }
        }
        rule.onNodeWithTag("Editor.Text").performTouchInput { click() }
        rule.waitUntil(8_000) {
            device.executeShellCommand("dumpsys input_method").contains("mInputShown=true")
        }
        capture("homework-ime")
        val count = rule.onNodeWithTag("Editor.Count", true).fetchSemanticsNode()
        rule.runOnIdle { RenderedTextEvidence.check(count, 2f) }
        val surface = rule.onNodeWithTag("Sheet.Homework").fetchSemanticsNode()
        rule.runOnIdle {
            val screen = surface.layoutInfo.coordinates.localToWindow(Offset.Zero)
            val bars = rule.activity.windowManager.currentWindowMetrics.windowInsets
                .getInsetsIgnoringVisibility(WindowInsets.Type.statusBars())
            assertTrue("Sheet top ${screen.y} overlaps statusbar ${bars.top}", screen.y >= bars.top)
        }
        rule.onNodeWithTag("Editor.Cancel").performScrollTo()
        full("Editor.Cancel").performClick()
        rule.onNodeWithTag("Sheet.Homework").assertDoesNotExist()
    }
}
