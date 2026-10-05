package ru.bgtu_voenmeh.zapara.ui.maps

import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.sizeIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.onLongClick
import androidx.compose.ui.semantics.semantics
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

/** Measured outside the raster: the 48dp action can never cover route ink or marker labels. */
@Composable
internal fun RouteRoomAction(state: MapsUiState, onEvent: (MapsEvent) -> Unit) {
    val room = state.highlight ?: return
    if (state.remote || state.showStack || state.presentation == null) return
    fun press() = onEvent(MapsEvent.PlanPress(room.rect.x + room.rect.w / 2, room.rect.y + room.rect.h / 2))
    Box(Modifier.background(Zapara.colors.card, RoundedCornerShape(Zapara.radii.chip))
        .sizeIn(minWidth = Zapara.space.minTouch, minHeight = Zapara.space.minTouch)
        .semantics { onLongClick { press(); true } }
        .testTag("Maps.Highlight")
        .pointerInput(room) { detectTapGestures(onLongPress = { press() }) }
        .padding(Zapara.space.s)) {
        Text(room.label, style = Zapara.typography.caption, color = Zapara.colors.text1)
    }
}
