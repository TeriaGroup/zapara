package ru.bgtu_voenmeh.zapara.ui.maps

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.theme.ZIconButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

@Composable
fun MapFullscreen(state: MapsUiState, onEvent: (MapsEvent) -> Unit) {
    Dialog(
        onDismissRequest = { onEvent(MapsEvent.Fullscreen(false)) },
        properties = DialogProperties(usePlatformDefaultWidth = false, decorFitsSystemWindows = false)
    ) {
        BackHandler { onEvent(MapsEvent.Fullscreen(false)) }
        Box(Modifier.fillMaxSize().background(Zapara.colors.canvas)) {
            ZoomableMap(state.planFile, state.highlight, state.zoom, { onEvent(MapsEvent.Transform(it)) }, Modifier.fillMaxSize())
            ZIconButton(
                R.drawable.ic_x, stringResource(R.string.maps_close),
                { onEvent(MapsEvent.Fullscreen(false)) }, "Maps.Close",
                Modifier.align(Alignment.TopEnd).padding(Zapara.space.l)
            )
        }
    }
}
