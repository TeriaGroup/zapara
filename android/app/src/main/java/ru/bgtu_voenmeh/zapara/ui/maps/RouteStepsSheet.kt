package ru.bgtu_voenmeh.zapara.ui.maps

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.stateDescription
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

@Composable
fun RouteStepsSheet(state: MapsUiState, onEvent: (MapsEvent) -> Unit) {
    val steps = state.presentation?.steps.orEmpty()
    val index = steps.indexOfFirst { it.id == state.activeStepId }
    val scroll = rememberLazyListState()
    LaunchedEffect(state.stepsOpen, state.activeStepId) {
        if (index >= 0) scroll.scrollToItem(index + 1)
    }
    ZBottomSheet({ onEvent(MapsEvent.CloseRouteSteps) }, "Maps.StepsSheet") {
        Text(stringResource(R.string.maps_all_steps), style = Zapara.typography.section, color = Zapara.colors.text1)
        LazyColumn(Modifier.fillMaxWidth().weight(1f, fill = false), state = scroll,
            verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            item(key = "plan-controls") {
                RoutePresentationProblems(state)
                MapsFloorControls(state, onEvent)
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
