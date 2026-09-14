package ru.bgtu_voenmeh.zapara.ui.maps

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.sizeIn
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.EmptyState
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.components.ZSegmented
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.ZIconButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import kotlin.math.roundToInt

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun MapsSection(state: MapsUiState, onEvent: (MapsEvent) -> Unit) {
    BoxWithConstraints(Modifier.fillMaxSize()) {
        val compact = MapsLayout.compact(maxWidth.value.roundToInt(), maxHeight.value.roundToInt())
        val collapsed = MapsLayout.collapsed(maxWidth.value.roundToInt(), maxHeight.value.roundToInt(), androidx.compose.ui.platform.LocalDensity.current.fontScale)
        val chromeMax = MapsLayout.chromeMaxDp(maxHeight.value.roundToInt()).dp
        val controlsWidth = minOf(MapsLayout.SideChromeWidth.dp, maxWidth / 2)
        Column(Modifier.fillMaxSize()) {
            if (!compact) ZTopBar(stringResource(R.string.nav_maps)) {
                ZIconButton(R.drawable.ic_map_pin, stringResource(R.string.maps_to_next), { onEvent(MapsEvent.ToNext) }, "Maps.ToNext")
            }
            if (compact) {
                Row(Modifier.weight(1f).fillMaxWidth()) {
                    MapsChrome(
                        state, onEvent,
                        Modifier
                            .width(controlsWidth)
                            .fillMaxHeight()
                            .verticalScroll(rememberScrollState())
                            .padding(horizontal = Zapara.space.l),
                        compact = collapsed, sidePane = true
                    )
                    MapsPlanPane(state, onEvent, Modifier.weight(1f).fillMaxHeight().padding(end = Zapara.space.l), compact = true, sideSteps = true)
                }
            } else {
                MapsChrome(state, onEvent, Modifier.heightIn(max = chromeMax).verticalScroll(rememberScrollState()).padding(horizontal = Zapara.space.l), compact = collapsed)
                MapsPlanPane(
                    state, onEvent,
                    Modifier
                        .fillMaxWidth()
                        .weight(1f)
                        .heightIn(min = MapsLayout.MinPlanHeight.dp)
                        .padding(horizontal = Zapara.space.l)
                )
            }
        }
    }
    if (state.fullscreen) MapFullscreen(state, onEvent) else MapsModals(state, onEvent)
}

@Composable
internal fun MapsModals(state: MapsUiState, onEvent: (MapsEvent) -> Unit) {
    if (state.stepsOpen) RouteStepsSheet(state, onEvent)
    state.picker?.let { RoutePickerSheet(it, onEvent) }
    state.planPick?.let { pick ->
        AlertDialog(
            onDismissRequest = { onEvent(MapsEvent.ClosePlanPick) },
            title = { Text(stringResource(R.string.maps_plan_pick_title, pick.label), style = Zapara.typography.section) },
            confirmButton = {
                ZButton(stringResource(R.string.maps_to), { onEvent(MapsEvent.PlanPickAs(RouteField.To)) })
            },
            dismissButton = {
                ZButton(stringResource(R.string.maps_from), { onEvent(MapsEvent.PlanPickAs(RouteField.From)) }, ghost = true)
            },
            containerColor = Zapara.colors.card
        )
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun MapsChrome(state: MapsUiState, onEvent: (MapsEvent) -> Unit, modifier: Modifier = Modifier, compact: Boolean = false, sidePane: Boolean = false) {
    val c = Zapara.colors
    Column(modifier, verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        if (sidePane) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(stringResource(R.string.nav_maps), style = Zapara.typography.section, color = c.text1, modifier = Modifier.weight(1f))
                ZIconButton(R.drawable.ic_map_pin, stringResource(R.string.maps_to_next), { onEvent(MapsEvent.ToNext) }, "Maps.ToNext")
            }
            MapsStepChrome(state, onEvent)
            if (!state.remote && !state.showStack) MapsZoomRow(onEvent, showFullscreen = true)
        }
        // Keep the complete selector rows at the scroll origin, not behind the route card.
        if (!compact) MapsFloorControls(state, onEvent)
        ZCard(Modifier.fillMaxWidth(), tag = "Maps.Route") {
            FlowRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                Column(if (compact) Modifier.fillMaxWidth() else Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                    RouteEnd(
                        stringResource(R.string.maps_from),
                        state.fromLabel,
                        stringResource(R.string.maps_from_hint),
                        { onEvent(MapsEvent.OpenFrom) },
                        "Maps.From"
                    )
                    RouteEnd(
                        stringResource(R.string.maps_to),
                        state.toLabel,
                        stringResource(R.string.maps_to_hint),
                        { onEvent(MapsEvent.OpenTo) },
                        "Maps.To"
                    )
                }
                Column(horizontalAlignment = Alignment.End, verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                    if (state.canSwap) {
                        ZChip(stringResource(R.string.maps_swap), onClick = { onEvent(MapsEvent.SwapEnds) }, tag = "Maps.Swap")
                    }
                    if (state.durationLabel.isNotBlank()) {
                        Text(
                            state.durationLabel,
                            style = Zapara.typography.caption,
                            color = c.text2,
                            modifier = Modifier.testTag("Maps.Duration")
                        )
                    }
                }
            }
        }
        if (state.contextLine.isNotBlank() && (state.mode == MapMode.NextLesson || state.mode == MapMode.Lesson)) {
            Text(state.contextLine, style = Zapara.typography.caption, color = c.text2, modifier = Modifier.testTag("Maps.Context"))
        }
        state.remoteNote?.let { Text(it, style = Zapara.typography.caption, color = c.text2) }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
internal fun MapsFloorControls(state: MapsUiState, onEvent: (MapsEvent) -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        Text(stringResource(R.string.maps_plan_selection), style = Zapara.typography.caption, color = Zapara.colors.text2)
        ZSegmented(state.buildings, state.buildings.indexOf(state.building).coerceAtLeast(0), { onEvent(MapsEvent.PickBuilding(it)) }, "Maps.Building")
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            state.floors.forEach { n ->
                ZChip("$n", selected = n == state.floor, onClick = { onEvent(MapsEvent.PickFloor(n)) }, tag = "Maps.Floor.$n")
            }
            ZChip(stringResource(R.string.maps_stack), selected = state.showStack, onClick = { onEvent(MapsEvent.ToggleStack) }, tag = "Maps.Stack")
        }
    }
}

@Composable
internal fun MapsPlanPane(state: MapsUiState, onEvent: (MapsEvent) -> Unit, modifier: Modifier = Modifier, compact: Boolean = false, sideSteps: Boolean = false) {
    BoxWithConstraints(modifier) {
        val stepHeight = maxHeight * MapsLayout.StepsFraction
        Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
        ZCard(Modifier.fillMaxWidth().weight(1f), padded = false) {
        Box(Modifier.fillMaxSize()) {
            when {
                state.remote -> EmptyState(
                    R.drawable.ic_map,
                    stringResource(R.string.maps_remote_title),
                    stringResource(R.string.maps_remote_hint),
                    tag = "Empty.Remote"
                )
                state.showStack -> CampusStack(state.route, state.building, state.floors, state.floorFiles,
                    rasterRevision = state.stackRasterRevision, presentation = state.presentation, activeFloor = state.floor,
                    onFloorSelect = { floor ->
                        onEvent(MapsEvent.PickFloor(floor))
                        onEvent(MapsEvent.ToggleStack)
                    }, onRetry = { onEvent(MapsEvent.RetryMaps) })
                state.planFile == null && state.loaded && !state.routeLoading -> Column(
                    Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(Zapara.space.l).testTag("Maps.MapUnavailable"),
                    verticalArrangement = Arrangement.spacedBy(Zapara.space.s)
                ) {
                    Text(stringResource(R.string.maps_map_unavailable), style = Zapara.typography.bodyStrong, color = Zapara.colors.text1)
                    Text(stringResource(R.string.maps_text_steps_available), style = Zapara.typography.body, color = Zapara.colors.text1)
                    ZButton(stringResource(R.string.maps_retry), { onEvent(MapsEvent.RetryMaps) }, tag = "Maps.Retry")
                }
                else -> androidx.compose.runtime.key(state.building, state.floor, state.planFile, state.stackRasterRevision) {
                    ZoomableMap(
                    state.planFile, state.highlight, state.zoom, { onEvent(MapsEvent.Transform(it)) },
                    path = state.path, stairMarkers = state.stairMarkers, fitGeneration = state.fitGeneration,
                    onLongPress = { nx, ny -> onEvent(MapsEvent.PlanPress(nx, ny)) },
                    presentation = state.presentation, floorKey = FloorKey(state.building, state.floor),
                    activeStepId = state.activeStepId,
                    onMapUnavailable = { onEvent(MapsEvent.MapDecodeFailed(FloorKey(state.building, state.floor))) }
                )
                }
            }
        }
        }
        if (!sideSteps) Column(Modifier.fillMaxWidth().heightIn(max = stepHeight).verticalScroll(rememberScrollState())) {
            MapsStepChrome(state, onEvent)
        }
        if (!compact && !state.remote && !state.showStack) MapsZoomRow(onEvent, showFullscreen = !state.fullscreen)
        }
    }
}

@Composable
private fun MapsStepChrome(state: MapsUiState, onEvent: (MapsEvent) -> Unit) {
            if (!state.remote && state.roomUnmarked) {
                Text(stringResource(R.string.maps_room_unmarked), style = Zapara.typography.caption, color = Zapara.colors.text2,
                    modifier = Modifier.testTag("Maps.RouteUnmarked"))
            }
            RouteStepBar(state, onEvent)
            RouteRoomAction(state, onEvent)
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
internal fun MapsZoomRow(onEvent: (MapsEvent) -> Unit, showFullscreen: Boolean) {
    ZCard(tag = "Maps.ZoomBar", padded = false) {
        FlowRow {
            ZIconButton(R.drawable.ic_plus, stringResource(R.string.maps_zoom_in), { onEvent(MapsEvent.ZoomIn) }, "Maps.ZoomIn")
            ZIconButton(R.drawable.ic_minus, stringResource(R.string.maps_zoom_out), { onEvent(MapsEvent.ZoomOut) }, "Maps.ZoomOut")
            ZChip(stringResource(R.string.maps_fit), selected = false, onClick = { onEvent(MapsEvent.Fit) }, tag = "Maps.Fit")
            if (showFullscreen) {
                ZIconButton(R.drawable.ic_fullscreen, stringResource(R.string.maps_fullscreen), { onEvent(MapsEvent.Fullscreen(true)) }, "Maps.Fullscreen")
            }
        }
    }
}

@Composable
private fun RouteEnd(title: String, value: String, hint: String, onClick: () -> Unit, tag: String) {
    val c = Zapara.colors
    val empty = value.isBlank()
    Column(
        Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .testTag(tag)
            .sizeIn(minHeight = Zapara.space.minTouch)
            .padding(vertical = Zapara.space.xs)
    ) {
        Text(title, style = Zapara.typography.caption, color = c.text2)
        Text(
            if (empty) hint else value,
            style = Zapara.typography.body,
            color = if (empty) c.text3 else c.text1,
            modifier = Modifier.fillMaxWidth()
        )
    }
}
