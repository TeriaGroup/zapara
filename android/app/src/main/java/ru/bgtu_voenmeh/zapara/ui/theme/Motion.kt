package ru.bgtu_voenmeh.zapara.ui.theme

import android.database.ContentObserver
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import androidx.compose.animation.core.CubicBezierEasing
import androidx.compose.runtime.*
import androidx.compose.ui.platform.LocalContext

@Immutable
data class MotionSettings(val appSwitch: Boolean, val systemScale: Float) {
    val enabled: Boolean get() = appSwitch && systemScale > 0f
    fun ms(base: Int): Int {
        require(base >= 0) { "Duration must be non-negative" }
        return if (enabled) base else 0
    }
    companion object {
        val On = MotionSettings(true, 1f)
        val Off = MotionSettings(false, 0f)
    }
}

val LocalMotion = staticCompositionLocalOf { MotionSettings.Off }
val ZaparaEase = CubicBezierEasing(0.2f, 0.8f, 0.2f, 1f)

object Durations {
    const val section = 180
    const val indicator = 200
    const val press = 120
    const val theme = 220
    const val cascade = 200
    const val cascadeStep = 30
    const val cascadeWindow = 400
    const val cascadeCount = 8
    const val skeleton = 1400
    const val homework = 1600
    const val map = 1200
    const val zoom = 200
}

/** Observe changes while composed; unregister on disposal. No global system setting writes. */
@Composable
fun rememberMotionSettings(appSwitch: Boolean): MotionSettings {
    val resolver = LocalContext.current.contentResolver
    fun readScale() = Settings.Global.getFloat(resolver, Settings.Global.ANIMATOR_DURATION_SCALE, 1f)
    var scale by remember(resolver) { mutableFloatStateOf(readScale()) }
    DisposableEffect(resolver) {
        val observer = object : ContentObserver(Handler(Looper.getMainLooper())) {
            override fun onChange(selfChange: Boolean) { scale = readScale() }
        }
        resolver.registerContentObserver(Settings.Global.getUriFor(Settings.Global.ANIMATOR_DURATION_SCALE), false, observer)
        scale = readScale()
        onDispose { resolver.unregisterContentObserver(observer) }
    }
    return MotionSettings(appSwitch, scale)
}
