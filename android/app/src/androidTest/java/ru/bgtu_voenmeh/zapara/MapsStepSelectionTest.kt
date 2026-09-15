package ru.bgtu_voenmeh.zapara

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.campus.GraphPoint
import ru.bgtu_voenmeh.zapara.data.campus.Leg
import ru.bgtu_voenmeh.zapara.data.campus.Route
import ru.bgtu_voenmeh.zapara.ui.maps.*
import ru.bgtu_voenmeh.zapara.ui.theme.*

/** Synthetic state only: no Room/account/cache writes and no legacy instrumentation. */
class MapsStepSelectionTest {
    @get:Rule val rule = createComposeRule()
    private val a = GraphPoint(0.1, 0.2)
    private val b = GraphPoint(0.6, 0.4)
    private val route = Route(120, listOf(
        Leg("walk", "ГК", 1, null, null, listOf(a, b)),
        Leg("walk", "ГК", 1, null, null, listOf(b, a)),
        Leg("stair_up", "ГК", 1, "ГК", 2, listOf(a, a)),
        Leg("walk", "ГК", 2, null, null, listOf(a, b)),
        Leg("building_link", "ГК", 2, "УЛК", 2, listOf(b, a)),
        Leg("walk", "УЛК", 2, null, null, listOf(a, b))))
    private val presentation = RoutePresentationBuilder.build(route, null, null,
        listOf(FloorKey("ГК", 1), FloorKey("ГК", 2), FloorKey("УЛК", 2)).associateWith { RasterSize(100, 100) })
    private var state by mutableStateOf(MapsUiState(alphaMaps = true, loaded = true, route = route,
        presentation = presentation, activeStepId = presentation.steps.first().id))

    private fun event(event: MapsEvent) {
        state = when (event) {
            is MapsEvent.SelectRouteStep -> RouteNavigation.select(state, event.id)
            MapsEvent.NextRouteStep -> RouteNavigation.move(state, 1)
            MapsEvent.PreviousRouteStep -> RouteNavigation.move(state, -1)
            MapsEvent.OpenRouteSteps -> state.copy(stepsOpen = true)
            MapsEvent.CloseRouteSteps -> state.copy(stepsOpen = false)
            is MapsEvent.Fullscreen -> state.copy(fullscreen = event.on)
            is MapsEvent.PickFloor -> state.copy(floor = event.n)
            else -> state
        }
    }

    @Test fun actual_step_buttons_use_position_and_keep_route_identity() {
        rule.setContent { ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) { RouteStepBar(state, ::event) } }
        rule.onNodeWithTag("Maps.StepPrevious").assertIsNotEnabled()
        rule.onNodeWithTag("Maps.StepNext").assertHeightIsAtLeast(48.dp).performClick()
        rule.onNodeWithText("Шаг 2 из 5").assertExists()
        rule.runOnIdle { assertEquals(2, state.activeStepId); assertSame(route, state.route) }
        repeat(3) { rule.onNodeWithTag("Maps.StepNext").performClick() }
        rule.onNodeWithTag("Maps.StepNext").assertIsNotEnabled()
        rule.runOnIdle { assertEquals("УЛК", state.building); assertEquals(2, state.floor) }
        rule.onNodeWithTag("Maps.StepPrevious").performClick()
        rule.onNodeWithText("Шаг 4 из 5").assertExists()
    }

    @Test fun sheet_selects_stable_id_closes_and_reopens_at_selection() {
        rule.setContent { ZaparaTheme(ThemeChoice.Dark, MotionSettings.Off) {
            RouteStepBar(state, ::event)
            if (state.stepsOpen) RouteStepsSheet(state, ::event)
        } }
        rule.onNodeWithTag("Maps.AllSteps").performClick()
        rule.onNodeWithTag("Maps.Step.3").performScrollTo().performClick()
        rule.onNodeWithTag("Maps.StepsSheet").assertDoesNotExist()
        rule.runOnIdle { assertEquals(3, state.activeStepId); assertSame(route, state.route) }
        rule.onNodeWithTag("Maps.AllSteps").performClick()
        rule.onNodeWithTag("Maps.Step.3").assertIsDisplayed().assertIsSelected()
    }

    @Test fun large_font_missing_map_and_fullscreen_keep_accessible_actions() {
        rule.setContent { ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
            val density = LocalDensity.current
            CompositionLocalProvider(LocalDensity provides Density(density.density, 2f)) {
                Box(Modifier.size(320.dp, 600.dp)) { MapsSection(state, ::event) }
            }
        } }
        rule.onNodeWithTag("Maps.MapUnavailable").assertExists()
        rule.onNodeWithTag("Maps.AllSteps").assertIsDisplayed().assertHeightIsAtLeast(48.dp)
        rule.onNodeWithTag("Maps.Retry").performScrollTo().assertHeightIsAtLeast(48.dp)
        rule.onNodeWithTag("Maps.Fullscreen").performClick()
        rule.onNodeWithTag("Maps.Close").assertIsDisplayed().performClick()
        rule.runOnIdle { assertFalse(state.fullscreen); assertSame(route, state.route); assertEquals(0, state.activeStepId) }
    }

    @Test fun revisited_floor_is_selected_by_id_not_floor_number() {
        val repeated = Route(40, listOf(
            Leg("walk", "ГК", 1, null, null, listOf(a, b)),
            Leg("stair_up", "ГК", 1, "ГК", 2, listOf(b, b)),
            Leg("stair_down", "ГК", 2, "ГК", 1, listOf(b, b)),
            Leg("walk", "ГК", 1, null, null, listOf(b, a))))
        state = state.copy(route = repeated, presentation = RoutePresentationBuilder.build(repeated, null, null,
            listOf(FloorKey("ГК", 1), FloorKey("ГК", 2)).associateWith { RasterSize(100, 100) }), activeStepId = 0)
        rule.setContent { ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
            RouteStepBar(state, ::event)
            if (state.stepsOpen) RouteStepsSheet(state, ::event)
        } }
        rule.onNodeWithTag("Maps.AllSteps").performClick()
        rule.onNodeWithTag("Maps.Step.3").performScrollTo().performClick()
        rule.runOnIdle { assertEquals(3, state.activeStepId); assertEquals(1, state.floor); assertSame(repeated, state.route) }
        rule.onNodeWithTag("Maps.StepNext").assertIsNotEnabled()
        rule.onNodeWithTag("Maps.StepPrevious").performClick()
        rule.runOnIdle { assertEquals(2, state.activeStepId); assertEquals(2, state.floor); assertSame(repeated, state.route) }
    }

    @Test fun remote_does_not_claim_missing_map() {
        state = state.copy(remote = true, presentation = null, route = null)
        rule.setContent { ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) { MapsSection(state, ::event) } }
        rule.onNodeWithTag("Empty.Remote").assertExists()
        rule.onNodeWithTag("Maps.MapUnavailable").assertDoesNotExist()
    }
}
