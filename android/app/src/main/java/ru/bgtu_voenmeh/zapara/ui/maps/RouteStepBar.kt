package ru.bgtu_voenmeh.zapara.ui.maps

import androidx.compose.foundation.layout.*
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun RouteStepBar(state: MapsUiState, onEvent: (MapsEvent) -> Unit, modifier: Modifier = Modifier) {
    val presentation = state.presentation
    val steps = presentation?.steps.orEmpty()
    val index = steps.indexOfFirst { it.id == state.activeStepId }
    val step = steps.getOrNull(index)
    val collapsed = androidx.compose.ui.platform.LocalDensity.current.fontScale >= 1.5f ||
        androidx.compose.ui.platform.LocalConfiguration.current.screenHeightDp < MapsLayout.ShortHeightDp
    Column(modifier, verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        RoutePresentationProblems(state)
        if (collapsed) {
            FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                ZButton(stringResource(R.string.maps_all_steps), { onEvent(MapsEvent.OpenRouteSteps) }, tag = "Maps.AllSteps")
                if (step != null) Text(stringResource(R.string.maps_step_count, index + 1, steps.size),
                    style = Zapara.typography.bodyStrong, color = Zapara.colors.text1,
                    modifier = Modifier.padding(vertical = Zapara.space.s).testTag("Maps.ActiveStep"))
            }
            if (presentation?.arrived == true) Text(stringResource(R.string.maps_arrived), color = Zapara.colors.text1)
            RouteMarkerLegend(state)
            return@Column
        }
        if (state.routeLoading) Text(stringResource(R.string.maps_route_loading), color = Zapara.colors.text1)
        if (!state.remote) state.routeFailure?.let {
            Text(it, style = Zapara.typography.body, color = Zapara.colors.text1)
            ZButton(stringResource(R.string.maps_retry), { onEvent(MapsEvent.RetryMaps) }, tag = "Maps.RouteRetry")
        }
        if (presentation?.arrived == true) Text(stringResource(R.string.maps_arrived), color = Zapara.colors.text1)
        if (step != null) {
            Column(Modifier.testTag("Maps.ActiveStep")) {
                Text(stringResource(R.string.maps_step_count, index + 1, steps.size), style = Zapara.typography.bodyStrong, color = Zapara.colors.text1)
                RouteStepDescription(step, state.decodeFailedFloors)
            }
            val selected = RouteNavigation.select(state, step.id)
            if (selected.building != state.building || selected.floor != state.floor) {
                Text(stringResource(R.string.maps_other_floor), style = Zapara.typography.body, color = Zapara.colors.text1)
                ZButton(stringResource(R.string.maps_return_step), { onEvent(MapsEvent.SelectRouteStep(step.id)) }, tag = "Maps.ReturnStep")
            }
        }
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            if (steps.isNotEmpty()) {
                ZButton(stringResource(R.string.maps_step_previous), { onEvent(MapsEvent.PreviousRouteStep) },
                    enabled = index > 0, ghost = true, tag = "Maps.StepPrevious")
                ZButton(stringResource(R.string.maps_step_next), { onEvent(MapsEvent.NextRouteStep) },
                    enabled = index >= 0 && index < steps.lastIndex, ghost = true, tag = "Maps.StepNext")
            }
            ZButton(stringResource(R.string.maps_all_steps), { onEvent(MapsEvent.OpenRouteSteps) }, tag = "Maps.AllSteps")
        }
        RouteMarkerLegend(state)
    }
}

@Composable
internal fun RouteStepDescription(step: RouteStep, unavailable: Set<FloorKey> = emptySet()) {
    Text(routeStepText(step), style = Zapara.typography.body, color = Zapara.colors.text1)
    Text(stringResource(R.string.maps_step_context, step.from.building, step.from.floor, step.to.building, step.to.floor),
        style = Zapara.typography.caption, color = Zapara.colors.text2)
    val problems = if (step.from in unavailable || step.to in unavailable) step.problems + RouteProblem.MissingMap else step.problems
    routeProblemResources(problems).forEach { resource ->
        Text(stringResource(resource), style = Zapara.typography.body, color = Zapara.colors.text1)
    }
}

@Composable
internal fun routeStepText(step: RouteStep): String = when (step.kind) {
    RoutePartKind.Walk -> stringResource(R.string.maps_step_walk, step.from.building, step.from.floor)
    RoutePartKind.StairUp -> stringResource(R.string.maps_step_up, step.to.floor)
    RoutePartKind.StairDown -> stringResource(R.string.maps_step_down, step.to.floor)
    RoutePartKind.BuildingLink -> stringResource(R.string.maps_step_link, step.to.building, step.to.floor)
}
