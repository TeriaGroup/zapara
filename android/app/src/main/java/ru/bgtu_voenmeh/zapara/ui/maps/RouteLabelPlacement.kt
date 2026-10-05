package ru.bgtu_voenmeh.zapara.ui.maps

import kotlin.math.roundToInt
import ru.bgtu_voenmeh.zapara.ui.maps.HighlightGeometry.ChipBox

/** Final integer layout bounds are tested, never a pre-clamp proposal. Null means use the text legend. */
internal object RouteLabelPlacement {
    fun place(x: Float, y: Float, w: Float, h: Float, occupied: List<ChipBox>,
        viewport: ChipBox, gap: Float): ChipBox? {
        if (w > viewport.w || h > viewport.h || w <= 0 || h <= 0) return null
        val minX = kotlin.math.ceil(viewport.x).toFloat()
        val minY = kotlin.math.ceil(viewport.y).toFloat()
        val maxX = kotlin.math.floor(viewport.x + viewport.w - w).toFloat()
        val maxY = kotlin.math.floor(viewport.y + viewport.h - h).toFloat()
        if (minX > maxX || minY > maxY) return null
        val xs = listOf(x, x - w - gap, x + gap, minX, maxX) +
            occupied.flatMap { listOf(it.x - w - gap, it.x + it.w + gap) }
        val ys = listOf(y, y - h - gap, y + gap, minY, maxY) +
            occupied.flatMap { listOf(it.y - h - gap, it.y + it.h + gap) }
        return ys.flatMap { top -> xs.map { left ->
            ChipBox(left.roundToInt().toFloat().coerceIn(minX, maxX),
                top.roundToInt().toFloat().coerceIn(minY, maxY), w, h)
        } }.distinct().sortedBy { (it.x - x) * (it.x - x) + (it.y - y) * (it.y - y) }
            .firstOrNull { candidate -> occupied.none { HighlightGeometry.overlaps(candidate, it, gap) } }
    }

    fun routeBounds(strokes: List<List<PathPx>>, clearance: Float): List<ChipBox> = strokes.flatMap { points ->
        points.zipWithNext().map { (a, b) ->
            ChipBox(minOf(a.x, b.x) - clearance, minOf(a.y, b.y) - clearance,
                kotlin.math.abs(a.x - b.x) + 2 * clearance, kotlin.math.abs(a.y - b.y) + 2 * clearance)
        }
    }
}

internal fun routeMarkerGroups(presentation: RoutePresentation, floor: FloorKey): List<List<RouteMarker>> =
    (presentation.endpoints + presentation.steps.flatMap { it.markers }).distinct().filter {
        it.floor == floor && it.point.x.isFinite() && it.point.y.isFinite() &&
            it.point.x in 0.0..1.0 && it.point.y in 0.0..1.0
    }.groupBy { it.point }.values.toList()
