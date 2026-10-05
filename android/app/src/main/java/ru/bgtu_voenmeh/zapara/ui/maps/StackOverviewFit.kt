package ru.bgtu_voenmeh.zapara.ui.maps

/** Layout-only fit: never changes graph coordinates, orbit, or painter ordering. */
internal fun StackScene.fitOverview(width: Float, height: Float, padding: Float): StackScene {
    val points = floors.flatMap { it.points2 }
    if (points.isEmpty() || width <= padding * 2 || height <= padding * 2) return this
    val left = points.minOf { it.x }
    val right = points.maxOf { it.x }
    val top = points.minOf { it.y }
    val bottom = points.maxOf { it.y }
    if (right <= left || bottom <= top) return this
    val scale = minOf((width - padding * 2) / (right - left), (height - padding * 2) / (bottom - top))
    fun fitted(point: StackPoint2) = StackPoint2(
        width / 2.0 + (point.x - (left + right) / 2) * scale,
        height / 2.0 + (point.y - (top + bottom) / 2) * scale)
    return copy(floors = floors.map { it.copy(points2 = it.points2.map(::fitted)) },
        polylines = polylines.map { it.copy(points2 = it.points2.map(::fitted)) },
        markers = markers.map { it.copy(point2 = fitted(it.point2)) })
}
