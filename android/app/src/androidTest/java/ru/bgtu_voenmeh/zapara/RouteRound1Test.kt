package ru.bgtu_voenmeh.zapara

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.toPixelMap
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.text.TextLayoutResult
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.campus.GraphPoint
import ru.bgtu_voenmeh.zapara.data.CoordsRect
import ru.bgtu_voenmeh.zapara.ui.maps.*
import ru.bgtu_voenmeh.zapara.ui.theme.*

/** Isolated Compose host, synthetic state only; never MainActivity/Room/profile seeding. */
class RouteRound1Test {
    @get:Rule val rule = createComposeRule()
    private val floor = FloorKey("ГК", 1)

    @Test fun noncentral_start_without_highlight_tracks_layer_and_resize() {
        var width by mutableStateOf(280)
        var scale by mutableStateOf(1f)
        var pan by mutableStateOf(0f)
        var density = 1f
        val presentation = RoutePresentation(emptyList(), emptyList(),
            listOf(RouteMarker(RouteMarkerKind.Start, floor, GraphPoint(.25, .35))), false, emptySet())
        rule.setContent { ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
            density = LocalDensity.current.density
            Box(Modifier.size(width.dp, 200.dp).testTag("capture")) {
                RouteMapOverlay(presentation, floor, null,
                    HighlightGeometry.fit(width * density, 200 * density, 1000f, 500f),
                    Modifier.graphicsLayer(scaleX = scale, scaleY = scale, translationX = pan))
            }
        } }
        for ((w, zoom, translation) in listOf(Triple(280, 1f, 0f), Triple(280, 1.5f, 12f), Triple(240, .8f, -8f))) {
            rule.runOnIdle { width = w; scale = zoom; pan = translation * density }
            val pixels = rule.onNodeWithTag("capture").captureToImage().toPixelMap()
            val fitted = HighlightGeometry.fit(pixels.width.toFloat(), pixels.height.toFloat(), 1000f, 500f)
            val anchor = PathGeometry.afterLayer(PathPx(fitted.originX + fitted.drawnW * .25f,
                fitted.originY + fitted.drawnH * .35f), zoom, translation * density, 0f, pixels.width / 2f, pixels.height / 2f)
            val radius = 8f * density * zoom
            var ink = 0
            var totalX = 0f
            var totalY = 0f
            for (y in (anchor.y - radius - density * 2).toInt()..(anchor.y + radius + density * 2).toInt()) {
                for (x in (anchor.x - radius - density * 2).toInt()..(anchor.x + radius + density * 2).toInt()) {
                    if (x !in 0 until pixels.width || y !in 0 until pixels.height) continue
                    val color = pixels[x, y]
                    if (color.blue > color.red * 1.3f && color.alpha > .8f) {
                        ink++; totalX += x + .5f; totalY += y + .5f
                    }
                }
            }
            assertTrue("No start ink at graph anchor: width=$w zoom=$zoom", ink > 8)
            assertEquals(anchor.x, totalX / ink, 2f)
            assertEquals(anchor.y, totalY / ink, 2f)
        }
    }

    @Test fun unrepresented_problems_visible_at_large_font() {
        var problem by mutableStateOf(RouteProblem.MissingCoordinates)
        rule.setContent { ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
            val density = LocalDensity.current
            CompositionLocalProvider(LocalDensity provides Density(density.density, 2f)) {
                RouteStepBar(MapsUiState(presentation = RoutePresentation(emptyList(), emptyList(), emptyList(),
                    false, setOf(problem))), {})
            }
        } }
        rule.onNodeWithText("Для этого участка нет координат. Следуйте текстовым шагам").assertExists()
        rule.runOnIdle { problem = RouteProblem.UnsupportedLeg }
        rule.onNodeWithText("Участок нельзя показать на плане").assertExists()
    }

    @Test fun measured_edge_labels_do_not_cover_short_route_and_keep_full_words() {
        var height by mutableStateOf(110)
        var density = 1f
        val points = listOf(GraphPoint(.8, .8), GraphPoint(.88, .8))
        val presentation = RoutePresentation(emptyList(),
            listOf(RouteSegment(0, floor, RoutePartKind.Walk, points)),
            listOf(RouteMarker(RouteMarkerKind.Start, floor, points[0]),
                RouteMarker(RouteMarkerKind.Destination, floor, points[1])), false, emptySet())
        rule.setContent { ZaparaTheme(ThemeChoice.Dark, MotionSettings.Off) {
            density = LocalDensity.current.density
            CompositionLocalProvider(LocalDensity provides Density(density, 2f)) {
                Column(Modifier.size(320.dp, 500.dp)) {
                    Box(Modifier.fillMaxWidth().height(height.dp).testTag("map")) {
                        RouteMapOverlay(presentation, floor, null, FittedImage(0f, 0f, 320 * density, height * density))
                    }
                    Column(Modifier.verticalScroll(rememberScrollState())) {
                        RouteStepBar(MapsUiState(presentation = presentation), {})
                    }
                }
            }
        } }
        for (h in listOf(110, 45)) {
            rule.runOnIdle { height = h }
            val parent = rule.onNodeWithTag("map").fetchSemanticsNode().boundsInRoot
            val boxes = mutableListOf<androidx.compose.ui.geometry.Rect>()
            for (tag in listOf("Maps.Marker.Start", "Maps.Marker.Destination", "Maps.MarkerNumber.0", "Maps.MarkerNumber.1")) {
                // Layout measures full and compact alternatives; only the placed one is visible.
                if (!rule.onNodeWithTag(tag).isDisplayed()) continue
                val nodes = rule.onAllNodesWithTag(tag).fetchSemanticsNodes().filter { it.boundsInRoot.width > 0 && it.boundsInRoot.height > 0 }
                for (node in nodes) {
                    val box = node.boundsInRoot
                    assertTrue("$tag beyond final viewport", box.left >= parent.left && box.top >= parent.top &&
                        box.right <= parent.right + 1 && box.bottom <= parent.bottom + 1)
                    assertTrue("Visible labels overlap: $tag $box vs $boxes", boxes.none { it.overlaps(box) })
                    boxes += box
                    val pathBox = androidx.compose.ui.geometry.Rect(parent.left + 320 * density * .8f - 8 * density,
                        parent.top + h * density * .8f - 8 * density, parent.left + 320 * density * .88f + 8 * density,
                        parent.top + h * density * .8f + 8 * density)
                    assertFalse("Label hides short route", box.overlaps(pathBox))
                    if (tag.startsWith("Maps.Marker.")) {
                        val layouts = mutableListOf<TextLayoutResult>()
                        rule.onNodeWithTag(tag).performSemanticsAction(SemanticsActions.GetTextLayoutResult) { it(layouts) }
                        for (text in layouts) for (line in 0 until text.lineCount - 1) {
                            val end = text.getLineEnd(line)
                            val value = text.layoutInput.text.text
                            assertFalse("Word split: $value", end in 1 until value.length && value[end - 1].isLetter() && value[end].isLetter())
                        }
                    }
                }
            }
            rule.onNodeWithTag("Maps.MarkerLegend.0").performScrollTo().assertTextEquals("1. Начало")
            rule.onNodeWithTag("Maps.MarkerLegend.1").performScrollTo().assertTextEquals("2. Назначение")
        }
    }

    @Test fun compact_map_numbers_stay_within_marker_size_at_200() {
        var density = 1f
        val points = listOf(GraphPoint(.2, .2), GraphPoint(.8, .8))
        val presentation = RoutePresentation(emptyList(),
            listOf(RouteSegment(0, floor, RoutePartKind.Walk, points)),
            listOf(RouteMarker(RouteMarkerKind.Start, floor, points[0]),
                RouteMarker(RouteMarkerKind.Destination, floor, points[1])), false, emptySet())
        rule.setContent { ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
            density = LocalDensity.current.density
            CompositionLocalProvider(LocalDensity provides Density(density, 2f)) {
                Box(Modifier.size(320.dp, 240.dp).testTag("map")) {
                    RouteMapOverlay(presentation, floor, null, FittedImage(0f, 0f, 320 * density, 240 * density))
                }
            }
        } }
        val maxPx = 24 * density + 1f
        var seen = 0
        for (tag in listOf("Maps.MarkerNumber.0", "Maps.MarkerNumber.1")) {
            if (!rule.onNodeWithTag(tag).isDisplayed()) continue
            val box = rule.onNodeWithTag(tag).fetchSemanticsNode().boundsInRoot
            assertTrue("$tag height ${box.height} exceeds marker", box.height <= maxPx)
            assertTrue("$tag width ${box.width} exceeds marker", box.width <= maxPx)
            seen++
        }
        assertTrue("Compact map numbers must be placed", seen > 0)
    }

    @Test fun problems_survive_normal_fullscreen_and_sheet_without_duplicate_messages() {
        var state by mutableStateOf(MapsUiState(alphaMaps = true, presentation = RoutePresentation(emptyList(), emptyList(), emptyList(), false,
            setOf(RouteProblem.UnsupportedLeg, RouteProblem.InvalidGeometry))))
        rule.setContent { ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
            val density = LocalDensity.current
            CompositionLocalProvider(LocalDensity provides Density(density.density, 2f)) {
                MapsSection(state, { event -> state = when (event) {
                    is MapsEvent.Fullscreen -> state.copy(fullscreen = event.on)
                    MapsEvent.OpenRouteSteps -> state.copy(stepsOpen = true)
                    MapsEvent.CloseRouteSteps -> state.copy(stepsOpen = false)
                    else -> state
                } })
            }
        } }
        rule.onAllNodesWithText("Участок нельзя показать на плане").assertCountEquals(1)
        rule.onNodeWithTag("Maps.Fullscreen").performClick()
        val fullscreenProblem = hasText("Участок нельзя показать на плане") and hasAnyAncestor(isDialog())
        rule.onAllNodes(fullscreenProblem).assertCountEquals(1)
        rule.onNode(fullscreenProblem).performScrollTo().assertIsDisplayed()
        rule.onNode(hasTestTag("Maps.AllSteps") and hasAnyAncestor(isDialog())).performScrollTo().performClick()
        val sheetProblem = hasText("Участок нельзя показать на плане") and hasAnyAncestor(hasTestTag("Maps.StepsSheet"))
        // ZBottomSheet's clickable surface merges static child semantics; inspect the actual text node.
        rule.onAllNodes(sheetProblem, useUnmergedTree = true).assertCountEquals(1)
        rule.onNode(sheetProblem, useUnmergedTree = true).performScrollTo().assertIsDisplayed()
    }

    @Test fun coincident_arrived_endpoints_keep_one_complete_legend() {
        val point = GraphPoint(1.0, 1.0)
        val presentation = RoutePresentation(emptyList(), emptyList(), listOf(
            RouteMarker(RouteMarkerKind.Start, floor, point), RouteMarker(RouteMarkerKind.Destination, floor, point)), true, emptySet())
        rule.setContent { ZaparaTheme(ThemeChoice.Dark, MotionSettings.Off) {
            val density = LocalDensity.current
            CompositionLocalProvider(LocalDensity provides Density(density.density, 2f)) {
                Column(Modifier.size(320.dp, 500.dp).verticalScroll(rememberScrollState())) {
                    Box(Modifier.height(45.dp)) {
                        RouteMapOverlay(presentation, floor, null, FittedImage(0f, 0f, 320 * density.density, 45 * density.density))
                    }
                    RouteStepBar(MapsUiState(presentation = presentation), {})
                }
            }
        } }
        val text = androidx.test.platform.app.InstrumentationRegistry.getInstrumentation().targetContext.getString(R.string.maps_start_destination_marker)
        rule.onNodeWithTag("Maps.MarkerLegend.0").performScrollTo().assertTextEquals("1. $text")
        rule.onNodeWithTag("Maps.MarkerLegend.1").assertDoesNotExist()
    }

    @Test fun external_room_action_keeps_48dp_and_exact_long_press_anchor() {
        val room = HighlightUi(CoordsRect(.2, .3, .1, .2), "101 · ГК")
        var press: MapsEvent.PlanPress? = null
        rule.setContent { ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
            RouteRoomAction(MapsUiState(highlight = room, presentation = RoutePresentation(emptyList(), emptyList(), emptyList(), false, emptySet())),
                { if (it is MapsEvent.PlanPress) press = it })
        } }
        rule.onNodeWithTag("Maps.Highlight").assertWidthIsAtLeast(48.dp).assertHeightIsAtLeast(48.dp)
            .performTouchInput { longClick() }
        rule.runOnIdle { assertEquals(MapsEvent.PlanPress(.25, .4), press) }
    }
}
