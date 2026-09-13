package ru.bgtu_voenmeh.zapara.ui.maps

import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

@Composable
internal fun CampusStackLegend(
    building: String,
    floors: List<Int>,
    available: Set<Int>,
    loading: Boolean,
    requested: Set<Int>,
    presentation: RoutePresentation?,
    activeFloor: Int?,
    onFloorSelect: ((Int) -> Unit)?,
    onRetry: (() -> Unit)?,
    modifier: Modifier = Modifier
) {
    val initialFloor = floors.distinct().indexOf(activeFloor).coerceAtLeast(0)
    val showUnavailable = requested.isEmpty() || (!loading && available.isEmpty())
    val scroll = rememberLazyListState(initialFirstVisibleItemIndex = initialFloor + if (showUnavailable) 1 else 0)
    LazyColumn(modifier.testTag("Maps.StackLegend").padding(Zapara.space.s),
        state = scroll,
        verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        if (showUnavailable) item {
            Text(stringResource(R.string.maps_map_unavailable), style = Zapara.typography.bodyStrong, color = Zapara.colors.text1)
            Text(stringResource(R.string.maps_text_steps_available), style = Zapara.typography.body, color = Zapara.colors.text1)
            if (onRetry != null) ZButton(stringResource(R.string.maps_retry), onRetry, tag = "Maps.StackRetry")
        }
        items(floors.distinct(), key = { it }) { floor ->
            val chosen = floor == activeFloor
            val label = stringResource(R.string.maps_stack_floor, building, floor)
            val status = when {
                floor in available -> ""
                loading && floor in requested -> stringResource(R.string.maps_stack_loading)
                else -> stringResource(R.string.maps_stack_missing)
            }
            val text = listOf(label, if (chosen) stringResource(R.string.maps_stack_selected) else "", status)
                .filter { it.isNotEmpty() }.joinToString(" · ")
            val outline = if (chosen) Modifier.border(Zapara.space.hairline * 2, Zapara.colors.lineStrong,
                RoundedCornerShape(Zapara.radii.control)) else Modifier
            Column(verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                ZButton(text, { onFloorSelect?.invoke(floor) },
                    Modifier.fillMaxWidth().then(outline).semantics { selected = chosen },
                    enabled = onFloorSelect != null, ghost = true, tag = "Maps.StackFloor.$floor")
                presentation?.let { route ->
                    val key = FloorKey(building, floor)
                    route.steps.filter { it.from == key && it.kind != RoutePartKind.Walk }.forEach { step ->
                        Text(routeStepText(step), style = Zapara.typography.bodyStrong, color = Zapara.colors.text1)
                    }
                    val markers = (route.endpoints + route.steps.flatMap { it.markers }).filter { it.floor == key }.distinct()
                    markers.forEach { marker ->
                        Text(markerText(marker), style = Zapara.typography.body, color = Zapara.colors.text1)
                    }
                }
            }
        }
    }
}
