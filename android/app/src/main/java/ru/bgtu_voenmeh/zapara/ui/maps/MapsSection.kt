package ru.bgtu_voenmeh.zapara.ui.maps

import androidx.compose.foundation.clickable
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.relocation.BringIntoViewRequester
import androidx.compose.foundation.relocation.bringIntoViewRequester
import androidx.compose.foundation.background
import androidx.compose.foundation.horizontalScroll
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
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.sizeIn
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.EmptyState
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.components.ZSegmented
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.components.ZTextField
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.ZIconButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZIcon
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import kotlin.math.roundToInt

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun MapsSection(state: MapsUiState, onEvent: (MapsEvent) -> Unit,
    onBackToLesson: (() -> Unit)? = null) {
    BoxWithConstraints(Modifier.fillMaxSize()) {
        val fontScale = androidx.compose.ui.platform.LocalDensity.current.fontScale
        val compact = MapsLayout.compact(maxWidth.value.roundToInt(), maxHeight.value.roundToInt())
        val collapsed = MapsLayout.collapsed(maxWidth.value.roundToInt(), maxHeight.value.roundToInt(), fontScale)
        val compactSteps = MapsLayout.compactSteps(maxWidth.value.roundToInt(), maxHeight.value.roundToInt(), fontScale)
        val chromeMax = MapsLayout.chromeMaxDp(maxHeight.value.roundToInt(), fontScale).dp
        val controlsWidth = minOf(MapsLayout.SideChromeWidth.dp, maxWidth / 2)
        Column(Modifier.fillMaxSize()) {
            if (!compact) ZTopBar(stringResource(R.string.nav_maps)) {
                if (onBackToLesson != null) ZIconButton(R.drawable.ic_chevron_left,
                    stringResource(R.string.ux30_maps_back_to_lesson), onBackToLesson, "Maps.BackToLesson")
                if (state.alphaMaps) {
                    ZIconButton(R.drawable.ic_map_pin, stringResource(R.string.maps_to_next), { onEvent(MapsEvent.ToNext) }, "Maps.ToNext")
                }
            }
            if (compact) {
                Row(Modifier.weight(1f).fillMaxWidth()) {
                    MapsChrome(
                        state, onEvent,
                        Modifier
                            .width(controlsWidth)
                            .fillMaxHeight()
                            .verticalScroll(rememberScrollState())
                            .padding(horizontal = Zapara.space.l, vertical = Zapara.space.s),
                        compact = collapsed, sidePane = true, compactSteps = compactSteps,
                        onBackToLesson = onBackToLesson
                    )
                    MapsPlanPane(state, onEvent, Modifier.weight(1f).fillMaxHeight().padding(end = Zapara.space.l), compact = true, sideSteps = true)
                }
            } else {
                MapsChrome(state, onEvent, Modifier.heightIn(max = chromeMax).verticalScroll(rememberScrollState()).padding(horizontal = Zapara.space.l, vertical = Zapara.space.s), compact = collapsed, compactSteps = compactSteps)
                MapsPlanPane(
                    state, onEvent,
                    Modifier
                        .fillMaxWidth()
                        .weight(1f)
                        .heightIn(min = MapsLayout.MinPlanHeight.dp)
                        .padding(horizontal = Zapara.space.l),
                    compactSteps = compactSteps
                )
            }
        }
    }
    if (state.fullscreen) MapFullscreen(state, onEvent) else MapsModals(state, onEvent)
}

@Composable
internal fun MapsModals(state: MapsUiState, onEvent: (MapsEvent) -> Unit) {
    if (!state.alphaMaps) return
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
private fun MapsChrome(state: MapsUiState, onEvent: (MapsEvent) -> Unit, modifier: Modifier = Modifier,
    compact: Boolean = false, sidePane: Boolean = false, compactSteps: Boolean = false,
    onBackToLesson: (() -> Unit)? = null) {
    val c = Zapara.colors
    Column(modifier, verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        if (sidePane) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(stringResource(R.string.nav_maps), style = Zapara.typography.section, color = c.text1, modifier = Modifier.weight(1f))
                if (state.alphaMaps) {
                    ZIconButton(R.drawable.ic_map_pin, stringResource(R.string.maps_to_next), { onEvent(MapsEvent.ToNext) }, "Maps.ToNext")
                }
            }
            if (state.alphaMaps) MapsStepChrome(state, onEvent, compactSteps)
            if (onBackToLesson != null) ZButton(stringResource(R.string.ux30_maps_back_to_lesson),
                onBackToLesson, ghost = true, quiet = true, tag = "Maps.BackToLesson")
            if (!state.remote && !state.showStack) MapsZoomRow(onEvent, showFullscreen = true, zoom = state.zoom, enabled = state.planFile != null)
        }
        // Keep the complete selector rows at the scroll origin, not behind the route card.
        MapsFloorControls(state, onEvent)
        if (state.alphaMaps) {
            if (state.canUndoEndpoint) ZButton(stringResource(R.string.ux300_ext_restore_point),
                { onEvent(MapsEvent.UndoEndpoint) }, ghost = true, tag = "Maps.UndoEndpoint")
            var historyOpen by rememberSaveable { mutableStateOf(false) }
            if (state.recentRoutes.isNotEmpty()) ZButton(stringResource(R.string.ux300_ext_recent_routes),
                { historyOpen = true }, ghost = true, tag = "Maps.RecentRoutes")
            if (historyOpen) ZBottomSheet({ historyOpen = false }, "Maps.RouteHistory", scrollable = true) {
                Text(stringResource(R.string.ux300_ext_recent_routes), style = Zapara.typography.section)
                Text(stringResource(R.string.ux300_ext_session_memory), style = Zapara.typography.caption)
                state.recentRoutes.forEach { route ->
                    ZButton(route.label, {
                        onEvent(MapsEvent.RepeatRoute(route.fromId, route.toId)); historyOpen = false
                    }, ghost = true, modifier = Modifier.fillMaxWidth())
                }
                ZButton(stringResource(R.string.ux300_ext_clear_routes), {
                    onEvent(MapsEvent.ClearRecentRoutes); historyOpen = false
                }, ghost = true)
            }
        }
        if (sidePane && state.alphaMaps) MapsRouteCard(state, onEvent, compact = true)
        state.remoteNote?.let { Text(it, style = Zapara.typography.caption, color = c.text2) }
        state.automaticNote?.let { note ->
            ZCard(Modifier.fillMaxWidth(), tag = "Maps.AutomaticNote") {
                Text(note, style = Zapara.typography.body, color = c.text1)
                if (!state.hasGroup && state.alphaMaps) ZButton(stringResource(R.string.group_pick),
                    LocalShellChrome.current.onGroupChip, ghost = true)
            }
        }
        state.mapError?.takeUnless { state.hasUnavailablePlan }?.let { error ->
            ZCard(Modifier.fillMaxWidth(), tag = "Maps.LoadError") {
                Text(error, style = Zapara.typography.body, color = c.text1)
                ZButton(stringResource(R.string.maps_retry), { onEvent(MapsEvent.RetryMaps) }, ghost = true)
            }
        }
    }
}

private val MapsUiState.hasUnavailablePlan: Boolean
    get() = !remote && !(alphaMaps && showStack) && planFile == null && loaded && !routeLoading

@OptIn(ExperimentalLayoutApi::class, ExperimentalFoundationApi::class)
@Composable
internal fun MapsFloorControls(state: MapsUiState, onEvent: (MapsEvent) -> Unit) {
    val planSelection = stringResource(R.string.maps_plan_selection)
    ZCard(Modifier.fillMaxWidth().semantics { contentDescription = planSelection }, tag = "Maps.Selectors", padded = false) {
    Column(Modifier.padding(Zapara.space.s), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        ZSegmented(state.buildings, state.buildings.indexOf(state.building).coerceAtLeast(0), { onEvent(MapsEvent.PickBuilding(it)) }, "Maps.Building", Modifier.fillMaxWidth())
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            Text(stringResource(R.string.maps_floor_label), style = Zapara.typography.caption, color = Zapara.colors.text2)
            Row(Modifier.weight(1f).horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            state.floors.forEach { n ->
                val floorDescription = "${stringResource(R.string.maps_floor_label)} $n"
                val bringIntoView = remember(state.building, n) { BringIntoViewRequester() }
                LaunchedEffect(state.building, state.floor) {
                    if (state.floor == n) bringIntoView.bringIntoView()
                }
                ZChip(
                    "$n",
                    modifier = Modifier.bringIntoViewRequester(bringIntoView).semantics { contentDescription = floorDescription },
                    selected = n == state.floor,
                    onClick = { onEvent(MapsEvent.PickFloor(n)) },
                    tag = "Maps.Floor.$n",
                    textStyle = Zapara.typography.bodyStrong
                )
            }
            }
            if (state.alphaMaps) {
                val stackDescription = stringResource(R.string.maps_stack)
                ZChip(stringResource(R.string.maps_stack_short), modifier = Modifier.semantics { contentDescription = stackDescription }, selected = state.showStack, onClick = { onEvent(MapsEvent.ToggleStack) }, tag = "Maps.Stack")
            }
        }
    }
    }
}

@Composable
internal fun MapsPlanPane(state: MapsUiState, onEvent: (MapsEvent) -> Unit, modifier: Modifier = Modifier, compact: Boolean = false, sideSteps: Boolean = false, compactSteps: Boolean = false) {
    var roomListOpen by rememberSaveable(state.building, state.floor) { mutableStateOf(false) }
    var roomQuery by rememberSaveable(state.building, state.floor) { mutableStateOf("") }
    BoxWithConstraints(modifier) {
        val stepHeight = maxHeight * MapsLayout.StepsFraction
        val narrowRoute = androidx.compose.ui.platform.LocalDensity.current.fontScale >= 1.5f || maxWidth < 360.dp
        // Let portrait route explanations use natural height; keep the fullscreen plan filling its pane.
        val scrollPlan = !compact && (!state.fullscreen || androidx.compose.ui.platform.LocalDensity.current.fontScale >= 1.5f)
        val planHeight = maxOf(MapsLayout.MinPlanHeight.dp, maxWidth * 0.75f)
        Column(Modifier.fillMaxSize().then(if (scrollPlan) Modifier.verticalScroll(rememberScrollState()) else Modifier),
            verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
        ZCard(Modifier.fillMaxWidth().then(if (scrollPlan) Modifier else Modifier.weight(1f)), padded = false) {
        Column(if (scrollPlan) Modifier.fillMaxWidth() else Modifier.fillMaxSize()) {
        MapsPlanHeading(state)
        Box(Modifier.fillMaxWidth().then(if (scrollPlan) Modifier.height(planHeight) else Modifier.weight(1f)).background(Zapara.colors.canvas)) {
            when {
                state.remote -> EmptyState(
                    R.drawable.ic_map,
                    stringResource(R.string.maps_remote_title),
                    stringResource(R.string.maps_remote_hint),
                    tag = "Empty.Remote"
                )
                state.alphaMaps && state.showStack -> CampusStack(state.route, state.building, state.floors, state.floorFiles,
                    rasterRevision = state.stackRasterRevision, presentation = state.presentation, activeFloor = state.floor,
                    onFloorSelect = { floor ->
                        onEvent(MapsEvent.PickFloor(floor))
                        onEvent(MapsEvent.ToggleStack)
                    }, onRetry = { onEvent(MapsEvent.RetryMaps) })
                state.hasUnavailablePlan -> Column(
                    Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(Zapara.space.l).testTag("Maps.MapUnavailable"),
                    verticalArrangement = Arrangement.spacedBy(Zapara.space.s)
                ) {
                    Text(stringResource(R.string.maps_map_unavailable), style = Zapara.typography.bodyStrong, color = Zapara.colors.text1)
                    state.mapError?.let { error ->
                        Text(error, style = Zapara.typography.body, color = Zapara.colors.text1,
                            modifier = Modifier.testTag("Maps.LoadError"))
                    }
                    Text(stringResource(if (state.alphaMaps && state.presentation != null) R.string.maps_text_steps_available else R.string.maps_unavailable_hint), style = Zapara.typography.body, color = Zapara.colors.text2)
                    ZButton(stringResource(R.string.maps_retry), { onEvent(MapsEvent.RetryMaps) }, tag = "Maps.Retry")
                }
                else -> androidx.compose.runtime.key(state.building, state.floor, state.planFile, state.stackRasterRevision) {
                    ZoomableMap(
                    state.planFile, state.highlight, state.zoom, { onEvent(MapsEvent.Transform(it)) },
                    path = if (state.alphaMaps) state.path else emptyList(),
                    stairMarkers = if (state.alphaMaps) state.stairMarkers else emptyList(),
                    fitGeneration = state.fitGeneration,
                    onLongPress = { nx, ny -> onEvent(MapsEvent.PlanPress(nx, ny)) },
                    presentation = if (state.alphaMaps) state.presentation else null,
                    floorKey = FloorKey(state.building, state.floor),
                    activeStepId = if (state.alphaMaps) state.activeStepId else null,
                    onMapUnavailable = { onEvent(MapsEvent.MapDecodeFailed(FloorKey(state.building, state.floor))) }
                )
                }
            }
        }
        if (!compact && !state.remote && !state.showStack && state.planFile != null) {
            Text(stringResource(R.string.maps_gestures_hint), style = Zapara.typography.caption, color = Zapara.colors.text2,
                modifier = Modifier.fillMaxWidth().padding(horizontal = Zapara.space.m, vertical = Zapara.space.s).testTag("Maps.GesturesHint"))
        }
        }
        }
        if (scrollPlan && !state.remote && !state.showStack) MapsZoomRow(onEvent, showFullscreen = !state.fullscreen, zoom = state.zoom, enabled = state.planFile != null)
        if (state.planFile != null && state.availableRooms.isNotEmpty() && !state.showStack)
            ZButton(stringResource(R.string.ux300_android_floor_rooms), { roomListOpen = true },
                ghost = true, tag = "Maps.FloorRooms")
        if (!sideSteps && state.alphaMaps) {
            val minStep = if (state.presentation != null || !state.fullscreen) Zapara.space.minTouch else 0.dp
            Column(
                Modifier
                    .fillMaxWidth()
                    .then(if (scrollPlan) Modifier else Modifier
                        .heightIn(min = minStep, max = maxOf(stepHeight, minStep))
                        .verticalScroll(rememberScrollState()))
            ) {
                if (!state.fullscreen) MapsRouteCard(state, onEvent, compact = narrowRoute)
                if (state.presentation != null || state.routeLoading || state.routeFailure != null || state.showStack || state.roomUnmarked) {
                    MapsStepChrome(state, onEvent, compactSteps)
                }
            }
        }
        if (!compact && !scrollPlan && !state.remote && !state.showStack) MapsZoomRow(onEvent, showFullscreen = !state.fullscreen, zoom = state.zoom, enabled = state.planFile != null)
        }
        if (roomListOpen) ZBottomSheet(onDismiss = { roomListOpen = false },
            tag = "Maps.FloorRoomSheet", scrollable = true) {
            Text(stringResource(R.string.ux300_android_floor_rooms),
                style = Zapara.typography.section)
            ZTextField(roomQuery, { roomQuery = it }, modifier = Modifier.fillMaxWidth()
                .testTag("Maps.RoomSearch"), singleLine = true,
                placeholder = { Text(stringResource(R.string.ux300_android_search_room)) })
            val matching = state.availableRooms.filter { it.room.contains(roomQuery.trim(), ignoreCase = true) }
                .distinctBy { it.id }.sortedBy { it.room }
            Text(stringResource(R.string.ux300_android_room_count, matching.size,
                state.availableRooms.size), style = Zapara.typography.caption)
            if (matching.isEmpty()) Text(stringResource(R.string.ux300_android_no_rooms),
                style = Zapara.typography.body)
            matching.take(30).forEach { room ->
                ZButton(room.room, {
                    onEvent(MapsEvent.FocusRoom(room.id)); roomListOpen = false
                }, ghost = true, modifier = Modifier.fillMaxWidth(),
                    tag = "Maps.FocusRoom.${room.id}")
            }
            if (matching.size > 30) Text(stringResource(R.string.ux300_android_refine_room),
                style = Zapara.typography.caption)
        }
    }
}

@Composable
private fun MapsPlanHeading(state: MapsUiState) {
    Row(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.m, vertical = Zapara.space.s),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        ZIcon(R.drawable.ic_map, null, Modifier.size(24.dp))
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            Text(if (state.remote) stringResource(R.string.maps_viewer_title)
                else if (state.showStack) stringResource(R.string.maps_stack)
                else stringResource(R.string.maps_stack_floor, state.building, state.floor),
                style = Zapara.typography.bodyStrong, color = Zapara.colors.text1,
                modifier = Modifier.testTag("Maps.PlanHeading"))
            if (state.contextLine.isNotBlank() && (state.mode == MapMode.NextLesson || state.mode == MapMode.Lesson)) {
                Text(state.contextLine, style = Zapara.typography.caption, color = Zapara.colors.text2,
                    modifier = Modifier.testTag("Maps.Context"))
            }
        }
    }
}

@Composable
private fun MapsStepChrome(state: MapsUiState, onEvent: (MapsEvent) -> Unit, compactSteps: Boolean = false) {
            if (!state.remote && state.roomUnmarked) {
                Text(stringResource(R.string.maps_room_unmarked), style = Zapara.typography.caption, color = Zapara.colors.text2,
                    modifier = Modifier.testTag("Maps.RouteUnmarked"))
            }
            RouteStepBar(state, onEvent, compact = compactSteps)
            RouteRoomAction(state, onEvent)
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
internal fun MapsZoomRow(onEvent: (MapsEvent) -> Unit, showFullscreen: Boolean, zoom: Float = 1f, enabled: Boolean = true) {
    val percentage = (zoom * 100).roundToInt()
    val scaleDescription = stringResource(R.string.maps_zoom_percentage, percentage)
    ZCard(tag = "Maps.ZoomBar", modifier = Modifier.fillMaxWidth(), padded = false) {
        FlowRow(Modifier.fillMaxWidth().padding(Zapara.space.s), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZIconButton(R.drawable.ic_minus, stringResource(R.string.maps_zoom_out), { onEvent(MapsEvent.ZoomOut) }, "Maps.ZoomOut", enabled = enabled && zoom > MapZoom.Min)
            Box(Modifier.widthIn(min = 48.dp).heightIn(min = Zapara.space.minTouch).semantics { contentDescription = scaleDescription }.testTag("Maps.Scale"), contentAlignment = Alignment.Center) {
                Text("$percentage%", style = Zapara.typography.bodyStrong, color = Zapara.colors.text2)
            }
            ZIconButton(R.drawable.ic_plus, stringResource(R.string.maps_zoom_in), { onEvent(MapsEvent.ZoomIn) }, "Maps.ZoomIn", enabled = enabled && zoom < MapZoom.Max)
            ZButton(stringResource(R.string.maps_fit_action), { onEvent(MapsEvent.Fit) }, ghost = true, tag = "Maps.Fit", enabled = enabled)
            if (showFullscreen) {
                ZIconButton(R.drawable.ic_fullscreen, stringResource(R.string.maps_fullscreen), { onEvent(MapsEvent.Fullscreen(true)) }, "Maps.Fullscreen", enabled = enabled)
            }
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun RouteMeta(state: MapsUiState, onEvent: (MapsEvent) -> Unit) {
    var now by remember(state.route) { mutableStateOf(java.time.LocalDateTime.now()) }
    LaunchedEffect(state.route) {
        while (state.route != null) {
            kotlinx.coroutines.delay(60_000)
            now = java.time.LocalDateTime.now()
        }
    }
    FlowRow(verticalArrangement = Arrangement.spacedBy(Zapara.space.xs),
        horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        if (state.durationLabel.isNotBlank()) {
            Text(state.durationLabel, style = Zapara.typography.caption, color = Zapara.colors.text2,
                modifier = Modifier.testTag("Maps.Duration"))
        }
        state.route?.let { route ->
            val arrival = now.plusSeconds(route.seconds.toLong().coerceAtLeast(0))
            val arrivalText = if (arrival.toLocalDate() == now.toLocalDate())
                stringResource(R.string.ux300_android_route_arrival,
                    arrival.format(java.time.format.DateTimeFormatter.ofPattern("HH:mm")))
            else stringResource(R.string.ux300_android_route_arrival_date,
                arrival.format(java.time.format.DateTimeFormatter.ofPattern("d MMM HH:mm",
                    java.util.Locale.forLanguageTag("ru"))))
            Text(arrivalText,
                style = Zapara.typography.caption, color = Zapara.colors.text2,
                modifier = Modifier.testTag("Maps.Arrival"))
        }
        if (state.canSwap) {
            ZChip(stringResource(R.string.maps_swap), onClick = { onEvent(MapsEvent.SwapEnds) }, tag = "Maps.Swap")
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

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun MapsRouteCard(state: MapsUiState, onEvent: (MapsEvent) -> Unit, compact: Boolean) {
    ZCard(Modifier.fillMaxWidth(), tag = "Maps.Route") {
            var expanded by rememberSaveable { mutableStateOf(false) }
            ZButton(stringResource(if (expanded) R.string.maps_route_collapse else R.string.maps_route_expand), { expanded = !expanded }, modifier = Modifier.fillMaxWidth(), ghost = true, quiet = true, tag = "Maps.RouteExpand")
            Column(verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                if (compact) {
                    RouteEnd(stringResource(R.string.maps_from), state.fromLabel, stringResource(R.string.maps_from_hint), { onEvent(MapsEvent.OpenFrom) }, "Maps.From")
                    RouteEnd(stringResource(R.string.maps_to), state.toLabel, stringResource(R.string.maps_to_hint), { onEvent(MapsEvent.OpenTo) }, "Maps.To")
                    if (expanded) RouteMeta(state, onEvent)
                } else {
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s), verticalAlignment = Alignment.CenterVertically) {
                        Box(Modifier.weight(1f)) {
                            RouteEnd(stringResource(R.string.maps_from), state.fromLabel, stringResource(R.string.maps_from_hint), { onEvent(MapsEvent.OpenFrom) }, "Maps.From")
                        }
                        Box(Modifier.weight(1f)) {
                            RouteEnd(stringResource(R.string.maps_to), state.toLabel, stringResource(R.string.maps_to_hint), { onEvent(MapsEvent.OpenTo) }, "Maps.To")
                        }
                    }
                    if (expanded) RouteMeta(state, onEvent)
                }
                FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                    verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                    if (state.canSwap) ZButton(stringResource(R.string.maps_swap),
                        { onEvent(MapsEvent.SwapEnds) }, ghost = true, tag = "Maps.SwapDirect")
                    if (state.fromLabel.isNotBlank()) ZButton(stringResource(R.string.ux100_platform_show_start),
                        { onEvent(MapsEvent.RevealEndpoint(RouteField.From)) }, ghost = true, tag = "Maps.ShowFrom")
                    if (state.toLabel.isNotBlank()) ZButton(stringResource(R.string.ux100_platform_show_destination),
                        { onEvent(MapsEvent.RevealEndpoint(RouteField.To)) }, ghost = true, tag = "Maps.ShowTo")
                    if (state.routeFailure != null) ZButton(stringResource(R.string.maps_retry),
                        { onEvent(MapsEvent.RetryMaps) }, ghost = true, tag = "Maps.RetryRouteCard")
                }
                if (expanded) FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    if (state.fromLabel.isNotBlank()) ZButton(stringResource(R.string.ux30_platform_clear_start),
                        { onEvent(MapsEvent.ClearEndpoint(RouteField.From)) }, ghost = true, tag = "Maps.ClearFrom")
                    if (state.toLabel.isNotBlank()) ZButton(stringResource(R.string.ux30_platform_clear_destination),
                        { onEvent(MapsEvent.ClearEndpoint(RouteField.To)) }, ghost = true, tag = "Maps.ClearTo")
                }
            }
        }
}
