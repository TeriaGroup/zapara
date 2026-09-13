package ru.bgtu_voenmeh.zapara

import androidx.compose.runtime.*
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.UiDevice
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.After
import java.io.File
import ru.bgtu_voenmeh.zapara.data.CoordsRect
import ru.bgtu_voenmeh.zapara.data.campus.*
import ru.bgtu_voenmeh.zapara.ui.maps.*
import ru.bgtu_voenmeh.zapara.ui.theme.*

/** Real fullscreen Dialog; in-memory state, no Room or profile seeding. Run on API37. */
class FullscreenControlsTest {
    @get:Rule val rule = createComposeRule()
    private val floor = FloorKey("ГК", 1)
    private val route = Route(120, listOf(
        Leg("walk", "ГК", 1, null, null, listOf(GraphPoint(.2, .3), GraphPoint(.6, .3))),
        Leg("stair_up", "ГК", 1, "ГК", 2, listOf(GraphPoint(.6, .3), GraphPoint(.6, .3))),
        Leg("walk", "ГК", 2, null, null, listOf(GraphPoint(.6, .3), GraphPoint(.7, .3))),
        Leg("stair_up", "ГК", 2, "ГК", 3, listOf(GraphPoint(.7, .3), GraphPoint(.7, .3))),
        Leg("walk", "ГК", 3, null, null, listOf(GraphPoint(.7, .3), GraphPoint(.8, .3))),
        Leg("stair_up", "ГК", 3, "ГК", 4, listOf(GraphPoint(.8, .3), GraphPoint(.8, .3)))))
    private val built = RoutePresentationBuilder.build(route, null, null,
        (1..4).associate { FloorKey("ГК", it) to RasterSize(100, 100) })
    private val presentation = built.copy(endpoints = listOf(RouteMarker(RouteMarkerKind.Start, floor, GraphPoint(.2, .3))) + built.endpoints)
    private var raster: File? = null
    @After fun cleanup() { raster?.delete() }
    private var state by mutableStateOf(MapsUiState(fullscreen = true, route = route,
        presentation = presentation, activeStepId = presentation.steps.first().id,
        highlight = HighlightUi(CoordsRect(.2, .3, .1, .2), "101 · ГК")))
    private var density = 1f
    private var press: MapsEvent.PlanPress? = null

    private fun event(event: MapsEvent) {
        state = when (event) {
            MapsEvent.ZoomIn -> state.copy(zoom = state.zoom * 1.25f)
            MapsEvent.ZoomOut -> state.copy(zoom = state.zoom / 1.25f)
            MapsEvent.Fit -> state.copy(zoom = 1f, fitGeneration = state.fitGeneration + 1)
            MapsEvent.OpenRouteSteps -> state.copy(stepsOpen = true)
            MapsEvent.CloseRouteSteps -> state.copy(stepsOpen = false)
            is MapsEvent.Fullscreen -> state.copy(fullscreen = event.on)
            is MapsEvent.PlanPress -> { press = event; state }
            else -> state
        }
    }

    private fun show(scale: Float) {
        rule.setContent { ZaparaTheme(ThemeChoice.Dark, MotionSettings.Off) {
            density = LocalDensity.current.density
            CompositionLocalProvider(LocalDensity provides Density(density, scale)) {
                if (state.fullscreen) MapFullscreen(state, ::event)
            }
        } }
    }

    private fun reachable(tag: String): SemanticsNodeInteraction {
        val node = rule.onNodeWithTag(tag)
        if (!node.isDisplayed()) node.performScrollTo()
        node.assertIsDisplayed().assertWidthIsAtLeast(48.dp).assertHeightIsAtLeast(48.dp)
        val visible = node.fetchSemanticsNode().boundsInRoot
        val full = node.getUnclippedBoundsInRoot()
        assertEquals("$tag horizontally clipped", (full.right - full.left).value * density, visible.width, 1f)
        assertEquals("$tag vertically clipped", (full.bottom - full.top).value * density, visible.height, 1f)
        val coordinates = node.fetchSemanticsNode().layoutInfo.coordinates
        val window = coordinates.localToWindow(androidx.compose.ui.geometry.Offset.Zero)
        val device = UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
        assertTrue("$tag outside physical screen: $window size=${coordinates.size} screen=${device.displayWidth}x${device.displayHeight}",
            window.x >= 0 && window.y >= 0 && window.x + coordinates.size.width <= device.displayWidth + 1 &&
                window.y + coordinates.size.height <= device.displayHeight + 1)
        return node
    }

    @Test fun landscape200_controls_are_fully_reachable_and_touch_changes_state() {
        val device = UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
        device.setOrientationLeft()
        rule.waitUntil(5000) { device.displayWidth > device.displayHeight }
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        raster = File.createTempFile("fullscreen-controls-", ".jpg", context.cacheDir).also { file ->
            context.assets.open("maps/karta-glavnyj-korpus-1-etazh-2022.jpg").use { input ->
                file.outputStream().use { input.copyTo(it) }
            }
        }
        state = state.copy(loaded = true, planFile = raster)
        show(2f)
        val actualDensity = context.resources.displayMetrics.density
        assertTrue("Actual height must be short", device.displayHeight / actualDensity < MapsLayout.ShortHeightDp)
        rule.onNodeWithTag("Maps.Plan").assertIsDisplayed().assertHeightIsAtLeast(MapsLayout.MinPlanHeight.dp)
        reachable("Maps.ZoomIn").performTouchInput { click() }
        rule.runOnIdle { assertTrue(state.zoom > 1f) }
        reachable("Maps.ZoomOut").performTouchInput { click() }
        rule.runOnIdle { assertEquals(1f, state.zoom, .001f) }
        reachable("Maps.Fit").performTouchInput { click() }
        rule.runOnIdle { assertEquals(1, state.fitGeneration) }
        rule.onNodeWithTag("Maps.MarkerLegend.1").performScrollTo().assertIsDisplayed()
        rule.onNodeWithTag("Maps.Highlight").performScrollTo()
        reachable("Maps.Highlight").performTouchInput { longClick() }
        rule.runOnIdle { assertEquals(MapsEvent.PlanPress(.25, .4), press) }
        rule.onNodeWithTag("Maps.AllSteps").performScrollTo()
        reachable("Maps.AllSteps").performTouchInput { click() }
        rule.onNodeWithTag("Maps.StepsSheet").assertIsDisplayed()
        device.pressBack()
        reachable("Maps.Close").performTouchInput { click() }
        rule.runOnIdle {
            assertFalse(state.fullscreen); assertSame(route, state.route)
            assertSame(presentation, state.presentation); assertEquals(presentation.steps.first().id, state.activeStepId)
        }
    }

    @Test fun portrait_controls_no_route_and_missing_map_remain_reachable() {
        val device = UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
        device.setOrientationNatural()
        rule.waitUntil(5000) { device.displayHeight > device.displayWidth }
        assertTrue("Run in portrait", device.displayHeight > device.displayWidth)
        state = MapsUiState(fullscreen = true, loaded = true)
        show(1f)
        reachable("Maps.ZoomIn").performTouchInput { click() }
        reachable("Maps.Fit").performTouchInput { click() }
        rule.onNodeWithTag("Maps.Retry").performScrollTo().assertIsDisplayed()
        reachable("Maps.Close").performTouchInput { click() }
        rule.runOnIdle { assertFalse(state.fullscreen); assertNull(state.route) }
    }
}
