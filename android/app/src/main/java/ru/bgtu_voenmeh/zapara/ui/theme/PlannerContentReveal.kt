package ru.bgtu_voenmeh.zapara.ui.theme

import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.tween
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.unit.dp

/** Reveal new planner content without replacing its composition or scroll state. */
@Composable
internal fun Modifier.plannerContentReveal(selection: Any): Modifier {
    val progress = remember { Animatable(1f) }
    var previous by remember { mutableStateOf(selection) }
    val motion = Zapara.motion
    val duration = motion.ms(Durations.section)
    LaunchedEffect(selection, duration) {
        val changed = previous != selection
        previous = selection
        if (duration == 0 || !changed) {
            progress.snapTo(1f)
        } else {
            progress.snapTo(0f)
            progress.animateTo(1f, tween(motion.ms(Durations.section), easing = ZaparaEase))
        }
    }
    return graphicsLayer {
        val fraction = if (duration == 0) 1f else progress.value
        alpha = 0.65f + 0.35f * fraction
        translationY = 8.dp.toPx() * (1f - fraction)
    }
}
