package ru.bgtu_voenmeh.zapara.ui.maps

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.theme.ZIconButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

@Composable
fun MapFullscreen(state: MapsUiState, onEvent: (MapsEvent) -> Unit) {
    Dialog(onDismissRequest = { onEvent(MapsEvent.Fullscreen(false)) },
        properties = DialogProperties(usePlatformDefaultWidth = false, decorFitsSystemWindows = false)) {
        BackHandler(enabled = !state.stepsOpen && state.picker == null && state.planPick == null) {
            onEvent(MapsEvent.Fullscreen(false))
        }
        BoxWithConstraints(Modifier.fillMaxSize().background(Zapara.colors.canvas).systemBarsPadding()) {
            val compact = MapsLayout.compact(maxWidth.value.toInt(), maxHeight.value.toInt())
            val compactSteps = MapsLayout.compactSteps(maxWidth.value.toInt(), maxHeight.value.toInt())
            val controlsWidth = minOf(MapsLayout.SideChromeWidth.dp, maxWidth / 2)
            if (compact) {
                Row(Modifier.fillMaxSize()) {
                    Column(Modifier.width(controlsWidth)
                        .fillMaxHeight().verticalScroll(rememberScrollState()).padding(Zapara.space.s),
                        verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        FullscreenClose(onEvent)
                        if (!state.remote && !state.showStack) MapsZoomRow(onEvent, showFullscreen = false)
                    }
                    MapsPlanPane(state, onEvent, Modifier.weight(1f).fillMaxHeight(), compact = true, compactSteps = compactSteps)
                }
            } else {
                Column(Modifier.fillMaxSize()) {
                    FullscreenClose(onEvent)
                    MapsPlanPane(state, onEvent, Modifier.fillMaxWidth().weight(1f), compactSteps = compactSteps)
                }
            }
        }
        MapsModals(state, onEvent)
    }
}

@Composable
private fun FullscreenClose(onEvent: (MapsEvent) -> Unit) {
    ZIconButton(R.drawable.ic_x, stringResource(R.string.maps_close),
        { onEvent(MapsEvent.Fullscreen(false)) }, "Maps.Close")
}
