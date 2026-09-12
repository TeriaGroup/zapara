package ru.bgtu_voenmeh.zapara.ui.maps

import kotlin.math.floor
import kotlin.math.hypot

data class RouteBounds(val left: Float, val top: Float, val right: Float, val bottom: Float)
data class RouteArrow(val tip: PathPx, val unitX: Float, val unitY: Float)
data class RasterSegment(
    val legIndex: Int,
    val floor: FloorKey,
    val points: List<PathPx>,
    val arrows: List<RouteArrow>,
    val bounds: RouteBounds?
)

/** Pure geometry of one local stroke; no Fit, density, zoom or inter-segment connections. */
object RouteGeometry {
    fun toRaster(segment: RouteSegment, size: RasterSize): RasterSegment {
        val drawable = (segment.kind == RoutePartKind.Walk || segment.kind == RoutePartKind.BuildingLink) &&
            segment.problems.none { it != RouteProblem.MissingMap } && segment.points.all {
                it.x.isFinite() && it.y.isFinite() && it.x in 0.0..1.0 && it.y in 0.0..1.0
            }
        val points = if (drawable) segment.points.map {
            PathPx((it.x * size.width).toFloat(), (it.y * size.height).toFloat())
        } else emptyList()
        return RasterSegment(segment.legIndex, segment.floor, points, arrows(points, 64f), bounds(points))
    }

    /** Pixel bounds also accept negative layout offsets; one invalid point rejects the whole stroke. */
    fun bounds(points: List<PathPx>): RouteBounds? {
        if (points.isEmpty() || points.any { !finite(it) }) return null
        return RouteBounds(points.minOf { it.x }, points.minOf { it.y },
            points.maxOf { it.x }, points.maxOf { it.y })
    }

    /** Samples spacing, 2*spacing, ... including an exact endpoint; short strokes use their arc midpoint.
     * At an exact corner the incoming nonzero edge supplies the tangent. Spacing must be finite and positive.
     */
    fun arrows(points: List<PathPx>, spacingPx: Float): List<RouteArrow> {
        require(spacingPx.isFinite() && spacingPx > 0f) { "spacingPx must be finite and positive" }
        if (points.size < 2 || points.any { !finite(it) }) return emptyList()
        // Double differences/lengths prevent overflow and preserve tiny nonzero Float edges.
        val lengths = DoubleArray(points.size - 1) { i ->
            hypot(points[i + 1].x.toDouble() - points[i].x, points[i + 1].y.toDouble() - points[i].y)
        }
        val total = lengths.sum()
        if (total == 0.0) return emptyList()
        val short = total < spacingPx
        val count = if (short) 1.0 else floor(total / spacingPx)
        // List sizes are Int. Reject an unrepresentable request before allocation or iteration.
        if (count > Int.MAX_VALUE) return emptyList()
        val result = ArrayList<RouteArrow>()
        var edge = 0
        var travelled = 0.0
        for (sample in 1..count.toInt()) {
            val distance = if (short) total / 2.0 else sample.toDouble() * spacingPx
            while (edge < lengths.lastIndex && (lengths[edge] == 0.0 || distance > travelled + lengths[edge])) {
                travelled += lengths[edge]
                edge++
            }
            val a = points[edge]
            val b = points[edge + 1]
            val dx = b.x.toDouble() - a.x
            val dy = b.y.toDouble() - a.y
            val length = lengths[edge]
            val fraction = (distance - travelled) / length
            result.add(RouteArrow(PathPx((a.x + dx * fraction).toFloat(), (a.y + dy * fraction).toFloat()),
                (dx / length).toFloat(), (dy / length).toFloat()))
        }
        return result
    }

    private fun finite(point: PathPx): Boolean = point.x.isFinite() && point.y.isFinite()
}
