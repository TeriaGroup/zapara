package ru.bgtu_voenmeh.zapara.ui.maps

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalView
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.compose.ui.platform.LocalLifecycleOwner
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
        var keepAwake by remember { mutableStateOf(false) }
        val view = LocalView.current
        val lifecycle = LocalLifecycleOwner.current.lifecycle
        val active = keepAwake && state.route != null && state.alphaMaps
        DisposableEffect(view, lifecycle, active) {
            val previous = view.keepScreenOn
            fun update() { view.keepScreenOn = previous || active && lifecycle.currentState.isAtLeast(Lifecycle.State.STARTED) }
            val observer = LifecycleEventObserver { _, _ -> update() }
            lifecycle.addObserver(observer)
            update()
            onDispose { lifecycle.removeObserver(observer); view.keepScreenOn = previous }
        }
        val awakeControl: @Composable () -> Unit = {
            if (state.route != null && state.alphaMaps) ru.bgtu_voenmeh.zapara.ui.theme.ZButton(
                stringResource(if (keepAwake) R.string.ux300_ext_allow_sleep else R.string.ux300_ext_keep_awake),
                { keepAwake = !keepAwake }, ghost = true, tag = "Maps.KeepAwake")
        }
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
                        awakeControl()
                        if (!state.remote && !state.showStack) MapsZoomRow(onEvent, showFullscreen = false, zoom = state.zoom, enabled = state.planFile != null)
                    }
                    MapsPlanPane(state, onEvent, Modifier.weight(1f).fillMaxHeight(), compact = true, compactSteps = compactSteps)
                }
            } else {
                Column(Modifier.fillMaxSize()) {
                    FullscreenClose(onEvent)
                    awakeControl()
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
