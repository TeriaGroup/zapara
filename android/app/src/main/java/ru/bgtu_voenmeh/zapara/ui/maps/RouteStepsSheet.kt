package ru.bgtu_voenmeh.zapara.ui.maps

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.Layout
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.constrainHeight
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

@Composable
fun RouteStepsSheet(state: MapsUiState, onEvent: (MapsEvent) -> Unit) {
    val steps = state.presentation?.steps.orEmpty()
    val index = steps.indexOfFirst { it.id == state.activeStepId }
    val scroll = rememberLazyListState()
    val keepControls = androidx.compose.ui.platform.LocalDensity.current.fontScale < 1.5f &&
        androidx.compose.ui.platform.LocalConfiguration.current.screenHeightDp >= MapsLayout.ShortHeightDp
    LaunchedEffect(state.stepsOpen, state.activeStepId) {
        if (index >= 0) scroll.scrollToItem(if (index == 0 && keepControls) 0 else index + 1)
    }
    ZBottomSheet({ onEvent(MapsEvent.CloseRouteSteps) }, "Maps.StepsSheet") {
        Text(stringResource(R.string.maps_all_steps), style = Zapara.typography.section, color = Zapara.colors.text1)
        LazyColumn(Modifier.fillMaxWidth().weight(1f, fill = false), state = scroll,
            verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            item(key = "plan-controls") {
                RoutePresentationProblems(state)
                MapsFloorControls(state) { event ->
                    onEvent(event)
                    if (event == MapsEvent.ToggleStack) onEvent(MapsEvent.CloseRouteSteps)
                }
                RouteMarkerLegend(state)
                if (!state.remote) state.routeFailure?.let {
                    Text(it, style = Zapara.typography.body, color = Zapara.colors.text1)
                    ru.bgtu_voenmeh.zapara.ui.theme.ZButton(stringResource(R.string.maps_retry),
                        { onEvent(MapsEvent.RetryMaps) }, tag = "Maps.RouteRetry")
                }
                if (state.routeLoading) Text(stringResource(R.string.maps_route_loading), color = Zapara.colors.text1)
            }
            itemsIndexed(steps, key = { _, step -> step.id }) { position, step ->
                val chosen = step.id == state.activeStepId
                val selectedLabel = stringResource(R.string.maps_selected_step)
                ZCard(Modifier.fillMaxWidth().sizeIn(minHeight = Zapara.space.minTouch).semantics {
                    selected = chosen
                    if (chosen) stateDescription = selectedLabel
                }, onClick = { onEvent(MapsEvent.SelectRouteStep(step.id)) }, tag = "Maps.Step.${step.id}") {
                    Text(stringResource(R.string.maps_step_count, position + 1, steps.size), style = Zapara.typography.bodyStrong)
                    if (chosen) Text(selectedLabel, style = Zapara.typography.caption)
                    RouteStepDescription(step, state.decodeFailedFloors)
                    if (chosen) {
                        RouteStepActions {
                            ru.bgtu_voenmeh.zapara.ui.theme.ZButton(stringResource(R.string.maps_step_previous),
                                { onEvent(MapsEvent.PreviousRouteStep) }, enabled = position > 0, ghost = true, tag = "Maps.StepPrevious")
                            ru.bgtu_voenmeh.zapara.ui.theme.ZButton(stringResource(R.string.maps_step_next),
                                { onEvent(MapsEvent.NextRouteStep) }, enabled = position < steps.lastIndex, ghost = true, tag = "Maps.StepNext")
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun RouteStepActions(content: @Composable () -> Unit) {
    val gap = Zapara.space.s
    Layout(content = content, modifier = Modifier.fillMaxWidth()) { measurables, constraints ->
        val gapPx = gap.roundToPx()
        // Decide using full labels before measuring against a smaller remainder of the row.
        val horizontal = measurables.sumOf { it.maxIntrinsicWidth(Constraints.Infinity) } + gapPx <= constraints.maxWidth
        val childConstraints = constraints.copy(
            minWidth = if (horizontal) 0 else constraints.maxWidth,
            minHeight = 0
        )
        val children = measurables.map { it.measure(childConstraints) }
        val height = if (horizontal) children.maxOfOrNull { it.height } ?: 0
            else children.sumOf { it.height } + gapPx * (children.size - 1).coerceAtLeast(0)
        layout(constraints.maxWidth, constraints.constrainHeight(height)) {
            var offset = 0
            children.forEach { child ->
                child.placeRelative(if (horizontal) offset else 0, if (horizontal) 0 else offset)
                offset += (if (horizontal) child.width else child.height) + gapPx
            }
        }
    }
}
