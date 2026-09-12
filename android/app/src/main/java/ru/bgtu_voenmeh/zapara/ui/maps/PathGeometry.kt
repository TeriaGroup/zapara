package ru.bgtu_voenmeh.zapara.ui.maps

import kotlin.math.sqrt

data class PathPx(val x: Float, val y: Float)

data class PathTraceFrame(
    val revealed: List<List<PathPx>>,
    val head: PathPx?,
    val start: PathPx?,
    val end: PathPx?,
    val complete: Boolean
)

/** Normalized 0–1 route points → layout pixels through ContentScale.Fit letterbox and graphicsLayer zoom. */
object PathGeometry {
    fun onLayout(x: Float, y: Float, fitted: FittedImage): PathPx =
        PathPx(fitted.originX + x * fitted.drawnW, fitted.originY + y * fitted.drawnH)

    fun afterLayer(px: PathPx, scale: Float, offsetX: Float, offsetY: Float, pivotX: Float, pivotY: Float): PathPx {
        val x = pivotX + (px.x - pivotX) * scale + offsetX
        val y = pivotY + (px.y - pivotY) * scale + offsetY
        return PathPx(x, y)
    }

    fun mapped(
        x: Float,
        y: Float,
        layoutW: Float,
        layoutH: Float,
        imageW: Float,
        imageH: Float,
        scale: Float,
        offsetX: Float,
        offsetY: Float
    ): PathPx {
        val local = onLayout(x, y, HighlightGeometry.fit(layoutW, layoutH, imageW, imageH))
        return afterLayer(local, scale, offsetX, offsetY, layoutW / 2f, layoutH / 2f)
    }

    /** One polyline per walk/link; disconnected wings stay separate. Null/empty/short → no strokes. */
    fun mapStrokes(
        strokes: List<List<Pair<Float, Float>>>?,
        layoutW: Float,
        layoutH: Float,
        imageW: Float,
        imageH: Float,
        scale: Float,
        offsetX: Float,
        offsetY: Float
    ): List<List<PathPx>> {
        if (strokes.isNullOrEmpty() || imageW <= 0f || imageH <= 0f || layoutW <= 0f || layoutH <= 0f) {
            return emptyList()
        }
        val fitted = HighlightGeometry.fit(layoutW, layoutH, imageW, imageH)
        val pivotX = layoutW / 2f
        val pivotY = layoutH / 2f
        val out = ArrayList<List<PathPx>>(strokes.size)
        for (points in strokes) {
            if (points.size < 2) continue
            out.add(
                points.map { (x, y) ->
                    afterLayer(onLayout(x, y, fitted), scale, offsetX, offsetY, pivotX, pivotY)
                }
            )
        }
        return out
    }

    fun length(points: List<PathPx>): Float {
        if (points.size < 2) return 0f
        var sum = 0f
        for (i in 1 until points.size) sum += dist(points[i - 1], points[i])
        return sum
    }

    fun strokesLength(strokes: List<List<PathPx>>): Float {
        var sum = 0f
        for (stroke in strokes) sum += length(stroke)
        return sum
    }

    fun prefix(points: List<PathPx>, distance: Float): List<PathPx> {
        if (points.isEmpty()) return emptyList()
        if (distance <= 0f) return listOf(points[0])
        val result = ArrayList<PathPx>(points.size)
        result.add(points[0])
        var remaining = distance
        for (i in 1 until points.size) {
            val a = points[i - 1]
            val b = points[i]
            val len = dist(a, b)
            if (len < 1e-6f) {
                result.add(b)
                continue
            }
            if (remaining >= len - 1e-6f) {
                result.add(b)
                remaining -= len
                if (remaining <= 1e-6f) return result
                continue
            }
            val t = remaining / len
            result.add(PathPx(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t))
            return result
        }
        return result
    }

    fun at(strokes: List<List<PathPx>>, progress: Float): PathTraceFrame {
        if (strokes.isEmpty()) return PathTraceFrame(emptyList(), null, null, null, true)
        var start: PathPx? = null
        var end: PathPx? = null
        for (stroke in strokes) {
            if (stroke.isEmpty()) continue
            if (start == null) start = stroke[0]
            end = stroke.last()
        }
        val total = strokesLength(strokes)
        if (total <= 1e-6f || start == null) return PathTraceFrame(emptyList(), start, start, end, true)
        val t = progress.coerceIn(0f, 1f)
        var remaining = t * total
        val revealed = ArrayList<List<PathPx>>()
        var head: PathPx? = start
        for (stroke in strokes) {
            val len = length(stroke)
            if (len <= 1e-6f) continue
            if (remaining >= len - 1e-6f) {
                revealed.add(stroke)
                head = stroke.last()
                remaining -= len
                continue
            }
            if (remaining > 1e-6f) {
                val cut = prefix(stroke, remaining)
                if (cut.size >= 2) revealed.add(cut)
                head = cut.lastOrNull() ?: stroke[0]
            }
            remaining = 0f
            break
        }
        return PathTraceFrame(revealed, head, start, end, t >= 1f - 1e-5f)
    }

    fun drawProgress(elapsedMs: Float, durationMs: Int): Float {
        if (durationMs <= 0) return 1f
        if (elapsedMs <= 0f) return 0f
        val t = elapsedMs / durationMs
        return if (t >= 1f) 1f else t
    }

    fun loopProgress(elapsedMs: Float, durationMs: Int, loopMs: Int): Float {
        if (durationMs <= 0 || loopMs <= 0) return 1f
        if (elapsedMs < durationMs) return 1f
        var along = (elapsedMs - durationMs) % loopMs
        if (along < 0f) along += loopMs
        return along / loopMs
    }

    fun dashOffset(elapsedMs: Float, cycleMs: Int, period: Float): Float {
        if (cycleMs <= 0 || period <= 0f) return 0f
        var along = elapsedMs % cycleMs
        if (along < 0f) along += cycleMs
        return along / cycleMs * period
    }

    private fun dist(a: PathPx, b: PathPx): Float {
        val dx = b.x - a.x
        val dy = b.y - a.y
        return sqrt(dx * dx + dy * dy)
    }
}
