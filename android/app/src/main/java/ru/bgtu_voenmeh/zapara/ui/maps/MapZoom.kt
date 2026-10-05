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

    /** Pan as a fraction of the fitted, zoomed image; independent of viewport pixels. */
    fun normalizedPan(
        x: Float, y: Float,
        viewportWidth: Float, viewportHeight: Float,
        imageWidth: Float, imageHeight: Float,
        zoom: Float
    ): Pair<Float, Float> {
        if (viewportWidth <= 0f || viewportHeight <= 0f || imageWidth <= 0f || imageHeight <= 0f || zoom <= 0f) return 0f to 0f
        val fit = min(viewportWidth / imageWidth, viewportHeight / imageHeight)
        val (boundedX, boundedY) = clampPan(x, y, viewportWidth, viewportHeight, imageWidth, imageHeight, zoom)
        return boundedX / (imageWidth * fit * zoom) to boundedY / (imageHeight * fit * zoom)
    }

    fun restoredPan(
        normalizedX: Float, normalizedY: Float,
        viewportWidth: Float, viewportHeight: Float,
        imageWidth: Float, imageHeight: Float,
        zoom: Float
    ): Pair<Float, Float> {
        if (viewportWidth <= 0f || viewportHeight <= 0f || imageWidth <= 0f || imageHeight <= 0f || zoom <= 0f) return 0f to 0f
        val fit = min(viewportWidth / imageWidth, viewportHeight / imageHeight)
        return clampPan(normalizedX * imageWidth * fit * zoom, normalizedY * imageHeight * fit * zoom,
            viewportWidth, viewportHeight, imageWidth, imageHeight, zoom)
    }

    @Suppress("UNUSED_PARAMETER")
    fun shouldResetView(oldW: Int, oldH: Int, newW: Int, newH: Int): Boolean {
        return false
    }
}
