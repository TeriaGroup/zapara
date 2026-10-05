package ru.bgtu_voenmeh.zapara.ui.theme

import android.os.SystemClock
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.tween
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.composed
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.SemanticsPropertyKey
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.delay

val LocalSectionEntry = staticCompositionLocalOf { 0L }
val AppearAlphaKey = SemanticsPropertyKey<Float>("AppearAlpha")
val IndicatorXKey = SemanticsPropertyKey<Float>("IndicatorX")
val ShineXKey = SemanticsPropertyKey<Float>("ShineX")

@Volatile
var cascadeLaunches: Int = 0

@Composable
fun ProvideSectionEntry(content: @Composable () -> Unit) {
    val entry = remember { SystemClock.uptimeMillis() }
    CompositionLocalProvider(LocalSectionEntry provides entry, content = content)
}

fun Modifier.appear(index: Int, entryTime: Long? = null, motion: MotionSettings? = null): Modifier = composed {
    val time = entryTime ?: LocalSectionEntry.current
    val m = motion ?: Zapara.motion
    val lift = with(LocalDensity.current) { 8.dp.toPx() }
    val skip = !m.enabled || index >= Durations.cascadeCount || SystemClock.uptimeMillis() - time > Durations.cascadeWindow
    if (skip) return@composed this.semantics { set(AppearAlphaKey, 1f) }
    val progress = remember { Animatable(0f) }
    LaunchedEffect(Unit) {
        cascadeLaunches++
        delay(index * Durations.cascadeStep.toLong())
        progress.animateTo(1f, tween(m.ms(Durations.cascade), easing = ZaparaEase))
    }
    val alpha = progress.value
    graphicsLayer {
        this.alpha = alpha
        translationY = (1f - alpha) * lift
    }.semantics { set(AppearAlphaKey, alpha) }
}
