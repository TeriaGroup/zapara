package ru.bgtu_voenmeh.zapara

import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.UiDevice
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.campus.*
import ru.bgtu_voenmeh.zapara.ui.maps.*
import ru.bgtu_voenmeh.zapara.ui.theme.*

/** No orientation, system setting, database or raster writes. */
class UxMapsChromeTest {
    @get:Rule val rule = createComposeRule()
    private val route = Route(120, listOf(
        Leg("walk", "ГК", 1, null, null, listOf(GraphPoint(.2, .3), GraphPoint(.6, .3))),
        Leg("stair_up", "ГК", 1, "ГК", 2, listOf(GraphPoint(.6, .3), GraphPoint(.6, .3))),
        Leg("walk", "ГК", 2, null, null, listOf(GraphPoint(.6, .3), GraphPoint(.8, .3)))))
    private val presentation = RoutePresentationBuilder.build(route, null, null,
        (1..2).associate { FloorKey("ГК", it) to RasterSize(100, 100) })
    private var state by mutableStateOf(MapsUiState(alphaMaps = true, loaded = true, route = route,
        presentation = presentation, activeStepId = presentation.steps.first().id,
        fromLabel = "Вход ГК", toLabel = "493 · ГК", canSwap = true, durationLabel = "около 3 мин"))
    private val events = mutableListOf<MapsEvent>()
    private var density = 1f

    // Reducer only reflects existing events: dismissal must originate in the actual UI.
    private fun event(value: MapsEvent) {
        events += value
        state = when (value) {
            MapsEvent.OpenRouteSteps -> state.copy(stepsOpen = true)
            MapsEvent.CloseRouteSteps -> state.copy(stepsOpen = false)
            MapsEvent.ToggleStack -> state.copy(showStack = !state.showStack)
            is MapsEvent.Fullscreen -> state.copy(fullscreen = value.on)
            is MapsEvent.PickFloor -> state.copy(floor = value.n)
            is MapsEvent.SelectRouteStep -> RouteNavigation.select(state, value.id)
            MapsEvent.PreviousRouteStep -> RouteNavigation.move(state, -1)
            MapsEvent.NextRouteStep -> RouteNavigation.move(state, 1)
            else -> state
        }
    }

    private fun show(scale: Float = 1f, short: Boolean = false, barOnly: Boolean = false) {
        rule.setContent { ZaparaTheme(ThemeChoice.Dark, MotionSettings.Off) {
            density = LocalDensity.current.density
            CompositionLocalProvider(LocalDensity provides Density(density, scale)) {
                // Bounded by the real host: never requiredSize or a fake low-density viewport.
                Box(Modifier.widthIn(max = 411.dp).height(if (short) 320.dp else 779.dp)
                    .testTag("UxMaps.Viewport")) {
                    if (barOnly) {
                        RouteStepBar(state, ::event)
                        if (state.stepsOpen) RouteStepsSheet(state, ::event)
                    } else if (state.fullscreen) MapFullscreen(state, ::event)
                    else MapsSection(state, ::event)
                }
            }
        } }
        rule.waitForIdle()
    }

    private fun control(tag: String): SemanticsNodeInteraction = if (state.stepsOpen)
        rule.onNode(hasTestTag(tag) and hasAnyAncestor(hasTestTag("Maps.StepsSheet")),
            useUnmergedTree = true)
    else rule.onNodeWithTag(tag)

    private fun fullyVisible(tag: String): SemanticsNodeInteraction {
        val node = control(tag).assertIsDisplayed()
            .assertWidthIsAtLeast(48.dp).assertHeightIsAtLeast(48.dp)
        val visible = node.fetchSemanticsNode().boundsInRoot
        val full = node.getUnclippedBoundsInRoot()
        assertEquals("$tag clipped width", (full.right - full.left).value * density, visible.width, 1f)
        assertEquals("$tag clipped height", (full.bottom - full.top).value * density, visible.height, 1f)
        assertTrue("$tag visible height below 48dp", visible.height >= 48f * density - 1f)
        val coordinates = node.fetchSemanticsNode().layoutInfo.coordinates
        val window = coordinates.localToWindow(Offset.Zero)
        val device = UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
        assertTrue("$tag outside physical viewport", window.x >= 0 && window.y >= 0 &&
            window.x + coordinates.size.width <= device.displayWidth + 1 &&
            window.y + coordinates.size.height <= device.displayHeight + 1)
        return node
    }

    private fun stepsList() = rule.onNode(hasScrollToIndexAction() and
        hasAnyAncestor(hasTestTag("Maps.StepsSheet")), useUnmergedTree = true)

    @Test fun normal_building_row_is_fully_visible_without_scroll() {
        show()
        fullyVisible("Maps.Building.0")
        fullyVisible("Maps.Building.1")
    }

    @Test fun persistent_step_omits_repeated_context_but_all_steps_keeps_it() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
            .getString(R.string.maps_step_context, "ГК", 1, "ГК", 1)
        show(barOnly = true)
        rule.onNodeWithText(context, useUnmergedTree = true).assertDoesNotExist()
        fullyVisible("Maps.AllSteps").performTouchInput { click() }
        rule.onNode(hasText(context) and hasAnyAncestor(hasTestTag("Maps.Step.0")),
            useUnmergedTree = true).assertIsDisplayed()
        val instruction = InstrumentationRegistry.getInstrumentation().targetContext
            .getString(R.string.maps_step_walk, "ГК", 1)
        rule.onNode(hasText(instruction) and hasAnyAncestor(hasTestTag("Maps.Step.0")),
            useUnmergedTree = true).assertIsDisplayed()
    }

    @Test fun missing_map_keeps_text_fallback_in_persistent_bar_and_selected_sheet_step() {
        state = state.copy(decodeFailedFloors = setOf(FloorKey("ГК", 1)))
        val fallback = InstrumentationRegistry.getInstrumentation().targetContext
            .getString(R.string.maps_map_unavailable)
        show(barOnly = true)
        rule.onNodeWithText(fallback, useUnmergedTree = true).assertIsDisplayed()
        rule.onNodeWithTag("Maps.AllSteps").performClick()
        rule.onNode(hasText(fallback) and hasAnyAncestor(hasTestTag("Maps.Step.0")),
            useUnmergedTree = true).assertIsDisplayed()
    }

    @Test fun selected_step_actions_share_row_when_their_natural_widths_fit() {
        state = state.copy(stepsOpen = true)
        show()
        val previous = fullyVisible("Maps.StepPrevious").fetchSemanticsNode().boundsInRoot
        val next = fullyVisible("Maps.StepNext").fetchSemanticsNode().boundsInRoot
        assertEquals("Unnecessarily stacked navigation", previous.top, next.top, 1f)
        assertTrue("Navigation overlaps", next.left >= previous.right)
        control("Maps.StepPrevious").assertIsNotEnabled()
        control("Maps.StepNext").assertIsEnabled()
    }

    @OptIn(androidx.compose.ui.ExperimentalComposeUiApi::class)
    @Test fun selected_last_step_at_200_percent_keeps_both_boundary_actions_reachable() {
        state = RouteNavigation.select(state, presentation.steps.last().id).copy(stepsOpen = true)
        show(scale = 2f)
        val viewport = rule.onNodeWithTag("UxMaps.Viewport").fetchSemanticsNode()
        val testActivity = rule.runOnIdle {
            var context = (viewport.root as androidx.compose.ui.platform.ViewRootForTest).view.context
            while (context is android.content.ContextWrapper && context !is android.app.Activity &&
                context.baseContext !== context) {
                context = context.baseContext
            }
            requireNotNull(context as? android.app.Activity) { "Viewport must belong to the test Activity" }
        }
        val matcher = hasText("Выбранный шаг") and hasAnyAncestor(hasTestTag("Maps.StepsSheet"))
        // Compose idleness does not establish platform Dialog window focus.
        rule.waitUntil(timeoutMillis = 5_000) {
            val node = rule.onAllNodes(matcher, true).fetchSemanticsNodes().singleOrNull()
            rule.runOnIdle {
                val view = (node?.root as? androidx.compose.ui.platform.ViewRootForTest)?.view
                view != null && view.isAttachedToWindow && view.hasWindowFocus() &&
                    view.rootView !== testActivity.window.decorView.rootView &&
                    ownsContext(testActivity, view.context)
            }
        }
        // Re-query after readiness; never reuse a snapshot from a polling iteration.
        val rendered = rule.onNode(matcher, true).assertIsDisplayed().fetchSemanticsNode()
        rule.runOnIdle { RenderedTextEvidence.check(rendered, 2f) }
        rule.onNodeWithTag("Maps.Step.${presentation.steps.last().id}").assertIsSelected()
        control("Maps.StepNext").performScrollTo()
        fullyVisible("Maps.StepNext").assertIsNotEnabled()
        control("Maps.StepPrevious").performScrollTo()
        fullyVisible("Maps.StepPrevious").assertIsEnabled().performTouchInput { click() }
        rule.runOnIdle {
            assertEquals(MapsEvent.PreviousRouteStep, events.last())
            assertEquals(presentation.steps[presentation.steps.lastIndex - 1].id, state.activeStepId)
            assertSame(route, state.route)
        }
    }

    @Test fun portrait_route_card_is_not_clipped_by_the_plan_at_200() {
        show(scale = 2f)
        val route = rule.onNodeWithTag("Maps.Route").assertIsDisplayed()
        val visible = route.fetchSemanticsNode().boundsInRoot
        val full = route.getUnclippedBoundsInRoot()
        assertEquals("Maps.Route clipped height", (full.bottom - full.top).value * density, visible.height, 1f)
        rule.onNodeWithTag("Maps.From").assertIsDisplayed()
        rule.onNodeWithTag("Maps.To").assertIsDisplayed()
        val viewport = rule.onNodeWithTag("UxMaps.Viewport").fetchSemanticsNode().boundsInRoot
        assertTrue("Route card is cut by the plan", visible.bottom <= viewport.bottom)
        assertTrue("Plan lost its minimum height", viewport.height - visible.height + 1f >= 160f * density)
    }

    @Test fun short_landscape_200_percent_preserves_160dp_map_and_fallback_actions() {
        show(scale = 2f, short = true)
        val viewport = rule.onNodeWithTag("UxMaps.Viewport").fetchSemanticsNode().boundsInRoot
        assertTrue("Host must fit the short landscape fixture", viewport.width > viewport.height)
        rule.onNodeWithTag("Maps.MapUnavailable").assertIsDisplayed().assertHeightIsAtLeast(160.dp)
        rule.onNodeWithTag("Maps.Retry").performScrollTo()
        fullyVisible("Maps.Retry").performTouchInput { click() }
        rule.runOnIdle { assertEquals(MapsEvent.RetryMaps, events.last()); assertSame(route, state.route) }
        rule.onNodeWithTag("Maps.AllSteps").performScrollTo()
        fullyVisible("Maps.AllSteps").performTouchInput { click() }
        rule.onNodeWithTag("Maps.Step.0").assertIsSelected()
    }

    @Test fun normal_sheet_scheme_action_dismisses_and_can_return_to_flat() = switchMode(false)
    @Test fun fullscreen_sheet_scheme_action_dismisses_and_can_return_to_flat() = switchMode(true)

    private fun switchMode(fullscreen: Boolean) {
        state = state.copy(fullscreen = fullscreen, stepsOpen = true)
        show()
        stepsList().performScrollToIndex(0)
        fullyVisible("Maps.Stack").performTouchInput { click() }
        rule.onNodeWithTag("Maps.StepsSheet").assertDoesNotExist()
        rule.onNodeWithTag("Maps.StackView").assertIsDisplayed()
        rule.runOnIdle {
            assertEquals(listOf(MapsEvent.ToggleStack, MapsEvent.CloseRouteSteps), events)
            assertTrue(state.showStack)
            events.clear()
        }
        rule.onNodeWithTag("Maps.AllSteps").performScrollTo()
        fullyVisible("Maps.AllSteps").performTouchInput { click() }
        stepsList().performScrollToIndex(0)
        fullyVisible("Maps.Stack").performTouchInput { click() }
        rule.onNodeWithTag("Maps.StepsSheet").assertDoesNotExist()
        rule.onNodeWithTag("Maps.StackView").assertDoesNotExist()
        rule.onNodeWithTag("Maps.MapUnavailable").assertIsDisplayed()
        rule.runOnIdle {
            assertEquals(listOf(MapsEvent.OpenRouteSteps, MapsEvent.ToggleStack, MapsEvent.CloseRouteSteps), events)
            assertFalse(state.showStack)
            assertEquals(fullscreen, state.fullscreen)
            assertEquals(presentation.steps.first().id, state.activeStepId)
            assertSame(route, state.route)
            assertSame(presentation, state.presentation)
        }
    }

    @Test fun settled_normal_sheet_header_does_not_overlap_revealed_plan_controls() = header(false)
    @Test fun settled_fullscreen_sheet_header_does_not_overlap_revealed_plan_controls() = header(true)

    @Test fun normal_direct_return_to_plan_preserves_route_selection_and_floor() = returnToPlan(false)
    @Test fun fullscreen_direct_return_to_plan_preserves_route_selection_and_floor() = returnToPlan(true)

    private fun returnToPlan(fullscreen: Boolean) {
        state = RouteNavigation.select(state, presentation.steps.last().id)
            .copy(fullscreen = fullscreen, showStack = true, stepsOpen = false)
        val before = state
        show()
        rule.onNodeWithTag("Maps.StackView").assertIsDisplayed()
        rule.onNodeWithTag("Maps.StepsSheet").assertDoesNotExist()
        fullyVisible("Maps.ReturnToPlan").assertIsEnabled().performTouchInput { click() }
        rule.onNodeWithTag("Maps.StackView").assertDoesNotExist()
        rule.onNodeWithTag("Maps.ReturnToPlan").assertDoesNotExist()
        rule.onNodeWithTag("Maps.StepsSheet").assertDoesNotExist()
        rule.onNodeWithTag("Maps.MapUnavailable").assertIsDisplayed()
        rule.runOnIdle {
            assertEquals(listOf(MapsEvent.ToggleStack), events)
            assertFalse(state.showStack)
            assertFalse(state.stepsOpen)
            assertEquals(fullscreen, state.fullscreen)
            assertEquals(before.activeStepId, state.activeStepId)
            assertEquals(before.building, state.building)
            assertEquals(before.floor, state.floor)
            assertSame(route, state.route)
            assertSame(presentation, state.presentation)
            assertEquals(before.copy(showStack = false), state)
        }
    }

    private fun header(fullscreen: Boolean) {
        state = state.copy(fullscreen = fullscreen)
        show()
        rule.onNodeWithTag("Maps.AllSteps").performScrollTo().performClick()
        rule.onNodeWithTag("Maps.Step.0").assertIsSelected()
        stepsList().performScrollToIndex(0)
        rule.waitForIdle()
        val title = rule.onNode(hasText("Все шаги") and
            hasAnyAncestor(hasTestTag("Maps.StepsSheet")), useUnmergedTree = true)
            .fetchSemanticsNode().boundsInRoot
        for (tag in listOf("Maps.Building.0", "Maps.Floor.1", "Maps.Stack")) {
            val control = fullyVisible(tag).fetchSemanticsNode().boundsInRoot
            assertTrue("$tag overlaps settled sheet header", control.top >= title.bottom)
        }
    }
}
