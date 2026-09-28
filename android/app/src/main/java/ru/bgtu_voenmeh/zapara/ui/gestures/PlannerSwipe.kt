package ru.bgtu_voenmeh.zapara.ui.gestures

import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Modifier
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.dp

/** Use on planner content only, so date tabs and unrelated drag surfaces retain their gestures. */
@Composable
internal fun Modifier.plannerSwipe(selection: Any, onSwipe: (PlannerSwipeDirection) -> Unit): Modifier {
    val latestOnSwipe by rememberUpdatedState(onSwipe)
    val density = LocalDensity.current
    val minimumDistance = with(density) { 64.dp.toPx() }
    val edgeWidth = with(density) { 24.dp.toPx() }
    // A selection change cancels an in-flight gesture before it can act on a stale date/parity.
    return pointerInput(selection, minimumDistance, edgeWidth) {
        awaitEachGesture {
            // Clickables may consume DOWN. Observe it without stealing a tap or long press.
            val down = awaitFirstDown(requireUnconsumed = false)
            val swipe = PlannerSwipePolicy(
                down.position.x, down.position.y, size.width.toFloat(),
                viewConfiguration.touchSlop, minimumDistance, edgeWidth
            )
            try {
                while (true) {
                    // Main lets child scrolling/drag controls consume movement before us.
                    val event = awaitPointerEvent(PointerEventPass.Main)
                    val change = event.changes.firstOrNull { it.id == down.id }
                    if (change == null || event.changes.size != 1) {
                        swipe.cancel()
                        break
                    }
                    val claimed = swipe.move(change.position.x, change.position.y, consumed = change.isConsumed)
                    if (!change.pressed) {
                        val direction = swipe.finish()
                        if (direction != null) {
                            change.consume()
                            latestOnSwipe(direction)
                        }
                        break
                    }
                    // Consumption begins after horizontal slop, cancelling child clicks.
                    if (claimed) change.consume()
                }
            } finally {
                swipe.cancel()
            }
        }
    }
}
