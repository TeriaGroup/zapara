package ru.bgtu_voenmeh.zapara

import androidx.compose.foundation.layout.fillMaxSize
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
import ru.bgtu_voenmeh.zapara.data.campus.*
import ru.bgtu_voenmeh.zapara.ui.maps.*
import ru.bgtu_voenmeh.zapara.ui.theme.*

/** Synthetic in-memory route. No Profile/Room mutation and no asset replacement. */
class CampusStackOverviewTest {
    @get:Rule val rule = createComposeRule()
    private val route = Route(20, listOf(Leg("building_link", "ГК", 1, "УЛК", 2,
        listOf(GraphPoint(.2, .4), GraphPoint(.8, .6)))))
    private val presentation = RoutePresentationBuilder.build(route, null, null, emptyMap())

    @Test fun no_floors_still_explains_and_retries() {
        var retried = false
        rule.setContent { ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
            CampusStack(null, "ГК", emptyList(), emptyMap(), onRetry = { retried = true })
        } }
        rule.onNodeWithTag("Maps.StackRetry").assertIsDisplayed().performClick()
        rule.runOnIdle { assertTrue(retried) }
    }

    @Test fun switching_building_resets_legend_to_selected_floor_and_keeps_route() {
        var building by mutableStateOf("УЛК")
        var floor by mutableIntStateOf(5)
        rule.setContent { ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
            CampusStack(route, building, if (building == "УЛК") (1..5).toList() else (1..4).toList(),
                emptyMap(), presentation = presentation, activeFloor = floor, onFloorSelect = {})
        } }
        rule.onNodeWithTag("Maps.StackFloor.5").assertIsDisplayed().assertIsSelected()
        rule.runOnIdle { building = "ГК"; floor = 1 }
        rule.onNodeWithTag("Maps.StackFloor.1").assertIsDisplayed().assertIsSelected()
            .assertTextContains("ГК · 1 этаж · Выбран · Нет плана")
        rule.onNodeWithTag("Maps.StackFloor.5").assertDoesNotExist()
    }

    @Test fun selected_floor_missing_plan_and_motion_off_selection_at_200_percent() {
        var picked: Int? = null
        rule.setContent { ZaparaTheme(ThemeChoice.Dark, MotionSettings.Off) {
            CompositionLocalProvider(LocalDensity provides Density(LocalDensity.current.density, 2f)) {
                CampusStack(route, "УЛК", listOf(1, 2, 3, 4, 5), emptyMap(),
                    presentation = presentation, activeFloor = 2, onFloorSelect = { picked = it })
            }
        } }
        rule.onNodeWithTag("Maps.StackFloor.2").assertIsSelected()
            .assertTextContains("УЛК · 2 этаж · Выбран · Нет плана")
            .assertWidthIsAtLeast(48.dp).assertHeightIsAtLeast(48.dp).performClick()
        rule.runOnIdle { assertEquals(2, picked) }
        rule.onNodeWithTag("Maps.StackLegend").performScrollToNode(hasTestTag("Maps.StackFloor.5"))
        rule.onNodeWithTag("Maps.StackFloor.5").assertIsDisplayed().assertHeightIsAtLeast(48.dp).performClick()
        rule.runOnIdle { assertEquals(5, picked) }
    }

    @Test fun empty_catalog_retry_and_local_departure_remain_accessible() {
        var retry = 0
        rule.setContent { ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
            CampusStack(route, "ГК", listOf(1), emptyMap(), presentation = presentation,
                activeFloor = 1, onFloorSelect = {}, onRetry = { retry++ })
        } }
        rule.onNodeWithTag("Maps.StackLegend").performScrollToIndex(0)
        rule.onNodeWithTag("Maps.StackRetry").performClick()
        rule.runOnIdle { assertEquals(1, retry) }
        rule.onNodeWithTag("Maps.StackLegend").performScrollToNode(hasText("Далее: УЛК, 2 этаж"))
        rule.onNodeWithText("Далее: УЛК, 2 этаж").assertIsDisplayed()
    }

    @Test fun embedded_floor_selection_returns_to_primary_without_losing_route() = integrated(false)
    @Test fun fullscreen_floor_selection_returns_to_primary_without_losing_route() = integrated(true)

    private fun integrated(fullscreen: Boolean) {
        var state by mutableStateOf(MapsUiState(building = "УЛК", floor = 2, floors = listOf(1, 2, 3, 4, 5),
            loaded = true, showStack = true, fullscreen = fullscreen, route = route,
            presentation = presentation, activeStepId = presentation.steps.first().id))
        val events = mutableListOf<MapsEvent>()
        fun event(value: MapsEvent) {
            events += value
            state = when (value) {
                is MapsEvent.PickFloor -> state.copy(floor = value.n)
                MapsEvent.ToggleStack -> state.copy(showStack = !state.showStack)
                else -> state
            }
        }
        rule.setContent { ZaparaTheme(ThemeChoice.Dark, MotionSettings.Off) {
            if (fullscreen) MapFullscreen(state, ::event)
            else MapsPlanPane(state, ::event, Modifier.fillMaxSize())
        } }
        rule.onNodeWithTag("Maps.StackLegend").performScrollToNode(hasTestTag("Maps.StackFloor.2"))
        rule.onNodeWithTag("Maps.StackFloor.2").assertIsSelected()
        rule.onNodeWithTag("Maps.StackLegend").performScrollToNode(hasTestTag("Maps.StackFloor.3"))
        rule.onNodeWithTag("Maps.StackFloor.3").performClick()
        rule.onNodeWithTag("Maps.StackView").assertDoesNotExist()
        rule.onNodeWithTag("Maps.MapUnavailable").assertIsDisplayed()
        rule.runOnIdle {
            assertEquals(listOf(MapsEvent.PickFloor(3), MapsEvent.ToggleStack), events)
            assertEquals(3, state.floor)
            assertFalse(state.showStack); assertEquals(fullscreen, state.fullscreen)
            assertSame(route, state.route); assertSame(presentation, state.presentation)
            assertEquals(presentation.steps.first().id, state.activeStepId)
        }
    }
}
