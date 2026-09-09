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
}
