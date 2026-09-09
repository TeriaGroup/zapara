package ru.bgtu_voenmeh.zapara.ui.maps

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.EmptyState
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.components.ZSegmented
import ru.bgtu_voenmeh.zapara.ui.shell.ZTopBar
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.ZIconButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

@Composable
fun MapsSection(state: MapsUiState, onEvent: (MapsEvent) -> Unit) {
    val c = Zapara.colors
    Column(Modifier.fillMaxSize()) {
        ZTopBar(stringResource(R.string.nav_maps)) {
            ZIconButton(R.drawable.ic_map_pin, stringResource(R.string.maps_to_next), { onEvent(MapsEvent.ToNext) }, "Maps.ToNext")
        }
        if (state.remote) {
            EmptyState(R.drawable.ic_map, stringResource(R.string.maps_remote_title), stringResource(R.string.maps_remote_hint), tag = "Empty.Remote")
        } else {
            Column(Modifier.padding(horizontal = Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                ZSegmented(state.buildings, state.buildings.indexOf(state.building).coerceAtLeast(0), { onEvent(MapsEvent.PickBuilding(it)) }, "Maps.Building")
                Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    state.floors.forEach { n ->
                        ZChip("$n", selected = n == state.floor, onClick = { onEvent(MapsEvent.PickFloor(n)) }, tag = "Maps.Floor.$n")
                    }
                }
                Text(state.contextLine, style = Zapara.typography.caption, color = c.text2, modifier = Modifier.testTag("Maps.Context"))
                state.remoteNote?.let { Text(it, style = Zapara.typography.caption, color = c.info) }
            }
            ZCard(Modifier.fillMaxWidth().padding(Zapara.space.l).weight(1f), padded = false) {
                Box(Modifier.fillMaxSize().aspectRatio(1.4f)) {
                    ZoomableMap(state.planFile, state.highlight, state.zoom, { onEvent(MapsEvent.Transform(it)) })
                    Column(Modifier.align(Alignment.BottomEnd).padding(Zapara.space.s), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                        ZIconButton(R.drawable.ic_plus, stringResource(R.string.maps_zoom_in), { onEvent(MapsEvent.ZoomIn) }, "Maps.ZoomIn")
                        ZIconButton(R.drawable.ic_minus, stringResource(R.string.maps_zoom_out), { onEvent(MapsEvent.ZoomOut) }, "Maps.ZoomOut")
                        ZIconButton(R.drawable.ic_fullscreen, stringResource(R.string.maps_fullscreen), { onEvent(MapsEvent.Fullscreen(true)) }, "Maps.Fullscreen")
                    }
                }
            }
        }
    }
    if (state.fullscreen) MapFullscreen(state, onEvent)
}
