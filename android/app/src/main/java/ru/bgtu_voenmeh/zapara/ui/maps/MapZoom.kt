package ru.bgtu_voenmeh.zapara.ui.maps

import kotlin.math.abs

object MapZoom {
    const val Min = 0.4f
    const val Max = 4f

    fun shown(gesture: Boolean, pinchScale: Float, animatedZoom: Float): Float =
        if (gesture) pinchScale else animatedZoom

    fun pinch(shown: Float, zoomChange: Float): Float =
        (shown * zoomChange).coerceIn(Min, Max)

    fun isButtonZoom(zoom: Float, lastEmitted: Float): Boolean =
        abs(zoom - lastEmitted) > 0.001f

    fun shouldResetView(oldW: Int, oldH: Int, newW: Int, newH: Int): Boolean {
        if (oldW <= 0 || oldH <= 0 || newW <= 0 || newH <= 0) return false
        return oldW != newW || oldH != newH
    }
}
