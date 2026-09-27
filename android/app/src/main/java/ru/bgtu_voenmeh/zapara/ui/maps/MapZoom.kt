package ru.bgtu_voenmeh.zapara.ui.maps

import kotlin.math.abs
import kotlin.math.min

object MapZoom {
    const val Min = 0.4f
    const val Max = 4f

    fun shown(gesture: Boolean, pinchScale: Float, animatedZoom: Float): Float =
        if (gesture) pinchScale else animatedZoom

    fun pinch(shown: Float, zoomChange: Float): Float =
        (shown * zoomChange).coerceIn(Min, Max)

    fun isButtonZoom(zoom: Float, lastEmitted: Float): Boolean =
        abs(zoom - lastEmitted) > 0.001f

    /** Translation limits belong to the fitted image, including its letterboxing. */
    fun clampPan(
        x: Float, y: Float,
        viewportWidth: Float, viewportHeight: Float,
        imageWidth: Float, imageHeight: Float,
        zoom: Float
    ): Pair<Float, Float> {
        if (viewportWidth <= 0f || viewportHeight <= 0f || imageWidth <= 0f || imageHeight <= 0f) {
            return 0f to 0f
        }
        val fit = min(viewportWidth / imageWidth, viewportHeight / imageHeight)
        val maxX = ((imageWidth * fit * zoom - viewportWidth) / 2f).coerceAtLeast(0f)
        val maxY = ((imageHeight * fit * zoom - viewportHeight) / 2f).coerceAtLeast(0f)
        return (if (maxX == 0f) 0f else x.coerceIn(-maxX, maxX)) to
            (if (maxY == 0f) 0f else y.coerceIn(-maxY, maxY))
    }

    fun shouldResetView(oldW: Int, oldH: Int, newW: Int, newH: Int): Boolean {
        if (oldW <= 0 || oldH <= 0 || newW <= 0 || newH <= 0) return false
        return oldW != newW || oldH != newH
    }
}
