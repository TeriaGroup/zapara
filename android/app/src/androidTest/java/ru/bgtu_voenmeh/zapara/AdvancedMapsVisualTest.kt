package ru.bgtu_voenmeh.zapara

import android.graphics.BitmapFactory
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Surface
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createEmptyComposeRule
import java.io.File
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.campus.*
import ru.bgtu_voenmeh.zapara.ui.maps.*
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ShellChrome
import ru.bgtu_voenmeh.zapara.ui.theme.MotionSettings
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme

/** Real bundled floor rasters with synthetic route state; never touches MapStore or saved routes. */
class AdvancedMapsVisualTest {
    @get:Rule val rule = createEmptyComposeRule()

    private val route = Route(120, listOf(
        Leg("walk", "ГК", 1, null, null, listOf(GraphPoint(.2, .3), GraphPoint(.6, .3))),
        Leg("stair_up", "ГК", 1, "ГК", 2, listOf(GraphPoint(.6, .3), GraphPoint(.6, .3))),
        Leg("walk", "ГК", 2, null, null, listOf(GraphPoint(.6, .3), GraphPoint(.8, .3)))))

    @Test fun route_steps_open_and_next_previous_follow_the_real_controls() = withPlans { plans ->
        val presentation = presentation(plans)
        var state by mutableStateOf(baseState(plans, presentation))
        OwnedTestHost.launch().use { host ->
            show(host, ThemeChoice.Light, state = { state }, onEvent = { event ->
                state = reduce(state, event, plans)
            })
            rule.onNodeWithTag("Maps.Route").assertIsDisplayed()
            rule.onNodeWithTag("Maps.MapUnavailable").assertDoesNotExist()
            rule.onNodeWithTag("Maps.AllSteps").performScrollTo()
            assertTrue(Frames.capture(host.activity, "fullqa-maps-route").length() > 1000)
            rule.onNodeWithTag("Maps.AllSteps").performClick()
            rule.onNodeWithTag("Maps.StepsSheet").assertIsDisplayed()
            val next = hasTestTag("Maps.StepNext") and hasAnyAncestor(hasTestTag("Maps.StepsSheet"))
            assertTrue(Frames.capture(host.activity, "fullqa-maps-steps").length() > 1000)
            rule.onNode(next, useUnmergedTree = true).performScrollTo().performClick()
            rule.runOnIdle { assertEquals(presentation.steps[1].id, state.activeStepId) }
            rule.onNodeWithTag("Maps.StepsSheet").assertDoesNotExist()
            rule.onNodeWithTag("Maps.StepPrevious").performScrollTo().performClick()
            rule.runOnIdle { assertEquals(presentation.steps.first().id, state.activeStepId) }
        }
    }

    @Test fun floor_selector_and_stack_show_bundled_plan_layers() = withPlans { plans ->
        val presentation = presentation(plans)
        var state by mutableStateOf(baseState(plans, presentation))
        OwnedTestHost.launch().use { host ->
            show(host, ThemeChoice.Dark, state = { state }, onEvent = { event ->
                state = reduce(state, event, plans)
            })
            rule.onNodeWithTag("Maps.Floor.2").performScrollTo().performClick().assertIsSelected()
            rule.runOnIdle { assertEquals(2, state.floor); assertEquals(plans.getValue(2), state.planFile) }
            assertTrue(Frames.capture(host.activity, "fullqa-maps-floors").length() > 1000)
            rule.onNodeWithTag("Maps.Stack").performScrollTo().performClick()
            rule.onNodeWithTag("Maps.StackView").assertIsDisplayed()
            val context = androidx.test.platform.app.InstrumentationRegistry.getInstrumentation().targetContext
            val loadedFloor = "${context.getString(R.string.maps_stack_floor, "ГК", 2)} · " +
                context.getString(R.string.maps_stack_selected)
            rule.waitUntil(15_000) {
                rule.onAllNodesWithText(loadedFloor, useUnmergedTree = true).fetchSemanticsNodes().isNotEmpty()
            }
            rule.onNodeWithText(loadedFloor, useUnmergedTree = true).assertIsDisplayed()
            assertTrue(Frames.capture(host.activity, "fullqa-maps-stack").length() > 1000)
        }
    }

    private fun show(host: OwnedTestHost, theme: ThemeChoice, state: () -> MapsUiState,
        onEvent: (MapsEvent) -> Unit) {
        host.scenario.onActivity { activity -> activity.setContent {
            ZaparaTheme(theme, MotionSettings.Off) {
                CompositionLocalProvider(LocalShellChrome provides ShellChrome("Тестовая группа", false, true) {}) {
                    Surface(Modifier.fillMaxSize().testTag("AdvancedMaps.Host"), color = Zapara.colors.canvas) {
                        MapsSection(state(), onEvent)
                    }
                }
            }
        } }
        rule.waitForIdle()
        host.awaitForeground()
    }

    private fun baseState(plans: Map<Int, File>, presentation: RoutePresentation) = MapsUiState(
        alphaMaps = true, loaded = true, hasGroup = true, building = "ГК", floors = listOf(1, 2),
        floor = 1, planFile = plans.getValue(1), floorFiles = plans,
        stackRasterRevision = runBlocking { StackRasterRevision.capture("ГК", plans) },
        rasterCatalog = plans.map { (floor, file) ->
            FloorKey("ГК", floor) to FloorRaster(file, rasterSize(file))
        }.toMap(),
        route = route, presentation = presentation, activeStepId = presentation.steps.first().id,
        fromLabel = "Вход ГК", toLabel = "493 · ГК", durationLabel = "около 3 мин")

    private fun reduce(state: MapsUiState, event: MapsEvent, plans: Map<Int, File>): MapsUiState = when (event) {
        MapsEvent.OpenRouteSteps -> state.copy(stepsOpen = true)
        MapsEvent.CloseRouteSteps -> state.copy(stepsOpen = false)
        MapsEvent.NextRouteStep -> RouteNavigation.move(state, 1)
        MapsEvent.PreviousRouteStep -> RouteNavigation.move(state, -1)
        is MapsEvent.SelectRouteStep -> RouteNavigation.select(state, event.id)
        is MapsEvent.PickFloor -> state.copy(floor = event.n, planFile = plans[event.n])
        MapsEvent.ToggleStack -> state.copy(showStack = !state.showStack)
        else -> state
    }

    private fun rasterSize(file: File): RasterSize {
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeFile(file.absolutePath, bounds)
        check(bounds.outWidth > 0 && bounds.outHeight > 0)
        return RasterSize(bounds.outWidth, bounds.outHeight)
    }

    private fun presentation(plans: Map<Int, File>): RoutePresentation {
        val sizes = plans.map { (floor, file) -> FloorKey("ГК", floor) to rasterSize(file) }.toMap()
        return RoutePresentationBuilder.build(route, null, null, sizes)
    }

    private fun withPlans(block: (Map<Int, File>) -> Unit) {
        val context = androidx.test.platform.app.InstrumentationRegistry.getInstrumentation().targetContext
        val directory = File(context.cacheDir, "qa-temp/advanced-maps-${System.nanoTime()}")
        check(directory.mkdirs())
        val plans = mapOf(
            1 to File(directory, "karta-glavnyj-korpus-1-etazh-2022.jpg"),
            2 to File(directory, "karta-glavnyj-korpus-2-etazh-2022.jpg"))
        try {
            for ((floor, file) in plans) {
                context.assets.open("maps/karta-glavnyj-korpus-$floor-etazh-2022.jpg").use { input ->
                    file.outputStream().use { output -> input.copyTo(output) }
                }
                check(file.length() > 0)
            }
            block(plans)
        } finally {
            plans.values.forEach { it.delete() }
            directory.delete()
        }
    }
}
