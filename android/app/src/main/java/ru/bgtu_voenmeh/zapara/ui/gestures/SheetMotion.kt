package ru.bgtu_voenmeh.zapara.ui.gestures

import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.tween
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.ui.theme.Durations
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaEase

@Stable
internal class SheetMotionState(initiallyAnimated: Boolean, private val allowDismiss: () -> Boolean = { true }) {
    val visibility = Animatable(if (initiallyAnimated) 0f else 1f)
    val returnProgress = Animatable(1f)
    val close = SheetClosePolicy()
    var closing by mutableStateOf(false)
        private set
    var dragging by mutableStateOf(false)
        private set
    var returnGeneration by mutableIntStateOf(0)
        private set
    private var dragOffset by mutableFloatStateOf(0f)

    val offset: Float get() = dragOffset * if (dragging) 1f else returnProgress.value

    fun startDrag() {
        if (closing) return
        dragOffset = 0f
        dragging = true
    }

    fun drag(offset: Float) {
        if (!closing) dragOffset = offset
    }

    fun finishDrag(dismiss: Boolean) {
        dragging = false
        returnGeneration++
        if (dismiss) requestDismiss()
    }

    fun requestDismiss() {
        if (close.request(allowDismiss)) {
            closing = true
            dragging = false
        }
    }

    fun withdrawDismiss() {
        close.withdraw()
        closing = false
    }
}

@Composable
internal fun rememberSheetMotion(onDismiss: () -> Unit, canDismiss: () -> Boolean = { true }): SheetMotionState {
    val motion = Zapara.motion
    val currentDismiss by rememberUpdatedState(onDismiss)
    val currentCanDismiss by rememberUpdatedState(canDismiss)
    val state = remember { SheetMotionState(motion.enabled) { currentCanDismiss() } }

    // Changing either motion switch cancels a running animation and snaps its target.
    LaunchedEffect(state.closing, motion.enabled) {
        val target = if (state.closing) 0f else 1f
        if (motion.enabled) {
            state.visibility.animateTo(target, tween(motion.ms(Durations.section), easing = ZaparaEase))
        } else {
            state.visibility.snapTo(target)
        }
        if (state.closing) {
            // A file picker or save can finish/start while the exit animation is running.
            // Re-check before delivery so a newly busy or edited form stays visible.
            if (!currentCanDismiss()) state.withdrawDismiss()
            else if (state.close.deliver()) currentDismiss()
        }
    }
    LaunchedEffect(state.dragging, state.returnGeneration, motion.enabled) {
        if (state.dragging) {
            state.returnProgress.snapTo(1f)
        } else if (motion.enabled) {
            state.returnProgress.animateTo(0f, tween(motion.ms(Durations.section), easing = ZaparaEase))
        } else {
            state.returnProgress.snapTo(0f)
        }
    }
    return state
}

/** Install only on the header handle: sheet content keeps its own pointer stream. */
@Composable
internal fun Modifier.sheetDragHandle(state: SheetMotionState): Modifier {
    val dismissDistance = with(LocalDensity.current) { 64.dp.toPx() }
    return pointerInput(state, dismissDistance) {
        awaitEachGesture {
            val down = awaitFirstDown(requireUnconsumed = false)
            if (state.closing || currentEvent.changes.size != 1) return@awaitEachGesture
            val policy = SheetDragPolicy(viewConfiguration.touchSlop, dismissDistance)
            var started = false
            var released = false
            var totalX = 0f
            var totalY = 0f
            try {
                while (true) {
                    val event = awaitPointerEvent()
                    val change = event.changes.firstOrNull { it.id == down.id }
                    if (event.changes.size != 1 || change == null || change.isConsumed || state.closing) {
                        policy.cancel()
                        break
                    }
                    // Both positions use the current local transform, so deltas stay
                    // stable while the sheet itself follows the finger.
                    totalX += change.position.x - change.previousPosition.x
                    totalY += change.position.y - change.previousPosition.y
                    val accepted = policy.move(totalX, totalY, event.changes.size)
                    if (accepted) {
                        if (!started) {
                            state.startDrag()
                            started = true
                        }
                        state.drag(totalY.coerceIn(0f, dismissDistance * 2.5f))
                        change.consume()
                    }
                    if (!change.pressed) {
                        released = true
                        break
                    }
                }
            } finally {
                if (!released) policy.cancel()
                if (started) state.finishDrag(policy.finish())
            }
        }
    }
}
