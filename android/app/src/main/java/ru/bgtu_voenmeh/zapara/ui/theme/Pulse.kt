package ru.bgtu_voenmeh.zapara.ui.theme

import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.runtime.Composable
import androidx.compose.runtime.State
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Modifier
import androidx.compose.ui.composed
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.unit.dp

@Composable
fun rememberPulse(active: Boolean, cycleMs: Int, from: Float, to: Float): State<Float> {
    val motion = Zapara.motion
    return if (active && motion.enabled) {
        val transition = rememberInfiniteTransition(label = "pulse")
        transition.animateFloat(
            initialValue = from,
            targetValue = to,
            animationSpec = infiniteRepeatable(tween(motion.ms(cycleMs) / 2, easing = ZaparaEase), RepeatMode.Reverse),
            label = "pulse"
        )
    } else {
        rememberUpdatedState(to)
    }
}

fun Modifier.breath(active: Boolean): Modifier = composed {
    val alpha by rememberPulse(active, Durations.homework, 0.55f, 1f)
    graphicsLayer { this.alpha = alpha }
}

fun Modifier.glow(active: Boolean, color: Color): Modifier = composed {
    val motion = Zapara.motion
    val a by rememberPulse(active, Durations.map, 0.4f, 0.8f)
    if (!active || !motion.enabled) this
    else drawBehind {
        drawRoundRect(color.copy(alpha = a), cornerRadius = CornerRadius(4.dp.toPx()))
    }
}
