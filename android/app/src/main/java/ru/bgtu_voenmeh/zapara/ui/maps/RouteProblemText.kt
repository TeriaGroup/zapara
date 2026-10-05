package ru.bgtu_voenmeh.zapara.ui.maps

import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

internal fun routeProblemResources(problems: Set<RouteProblem>): List<Int> = problems.map { problem ->
    when (problem) {
        RouteProblem.MissingMap -> R.string.maps_map_unavailable
        RouteProblem.MissingCoordinates -> R.string.maps_missing_coordinates
        RouteProblem.InvalidGeometry, RouteProblem.UnsupportedLeg -> R.string.maps_invalid_geometry
    }
}.distinct().sorted()

internal fun unrepresentedRouteProblems(presentation: RoutePresentation): List<Int> =
    routeProblemResources(presentation.problems) -
        routeProblemResources(presentation.steps.flatMap { it.problems }.toSet()).toSet()

@Composable
internal fun RoutePresentationProblems(state: MapsUiState) {
    if (state.remote) return
    state.presentation?.let { presentation ->
        unrepresentedRouteProblems(presentation).forEach { resource ->
            Text(stringResource(resource), style = Zapara.typography.body, color = Zapara.colors.text1,
                modifier = Modifier.testTag("Maps.PresentationProblem.$resource"))
        }
    }
}

@Composable
internal fun RouteMarkerLegend(state: MapsUiState) {
    if (state.remote || state.showStack) return
    state.presentation?.let { presentation ->
        routeMarkerGroups(presentation, FloorKey(state.building, state.floor)).forEachIndexed { index, group ->
            Text("${index + 1}. ${routeMarkerText(group)}", style = Zapara.typography.caption,
                color = Zapara.colors.text1, modifier = Modifier.testTag("Maps.MarkerLegend.$index"))
        }
    }
}
