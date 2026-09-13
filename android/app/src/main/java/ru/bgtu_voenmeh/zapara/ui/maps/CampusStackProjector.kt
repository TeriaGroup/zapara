package ru.bgtu_voenmeh.zapara.ui.maps

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import java.io.File
import ru.bgtu_voenmeh.zapara.data.campus.Route
import kotlin.math.cos
import kotlin.math.min
import kotlin.math.sin
import kotlin.math.PI

data class StackPoint3(val x: Double, val y: Double, val z: Double)

data class StackPoint2(val x: Double, val y: Double)

data class StackPolyline(
    val kind: String,
    val floor: Int,
    val toFloor: Int?,
    val points: List<StackPoint3>,
    val points2: List<StackPoint2> = emptyList()
)

data class StackQuad(
    val floor: Int,
    val points: List<StackPoint3>,
    val points2: List<StackPoint2>
)

data class StackScene(
    val floors: List<StackQuad>,
    val polylines: List<StackPolyline>,
    val markers: List<StackMarker> = emptyList()
) {
    fun withRasters(available: Set<Int>): StackScene = copy(
        polylines = polylines.filter { it.floor in available && (it.toFloor == null || it.toFloor in available) },
        markers = markers.filter { it.marker.floor.floor in available }
    )
}

data class StackMarker(val marker: RouteMarker, val point2: StackPoint2)

data class StackPaintItem(
    val kind: String,
    val floor: Int,
    val quad: StackQuad? = null,
    val line: StackPolyline? = null
)

/** Orthographic stack: floor quads + walk/stair polylines. No GPU. */
object CampusStackProjector {
    val DefaultYaw = (PI / 4).toFloat()
    val DefaultPitch = (PI / 6).toFloat()
    const val FloorGap = 0.55
    const val ThumbMaxEdge = 256

    fun floorRasters(building: String, fileForFloor: (Int) -> File?): Map<Int, File> {
        val shown = if (building == "ВЦ") "ГК" else building
        val out = LinkedHashMap<Int, File>()
        for (n in MapsComposer.floors(shown)) {
            val file = fileForFloor(n) ?: continue
            if (file.isFile && file.length() > 0L) out[n] = file
        }
        return out
    }

    fun decodeThumb(file: File, maxEdge: Int = ThumbMaxEdge): Bitmap? {
        if (maxEdge !in 1..ThumbMaxEdge || !file.isFile) return null
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeFile(file.absolutePath, bounds)
        if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return null
        val edge = maxOf(bounds.outWidth, bounds.outHeight).toLong()
        var sample = 1
        while ((edge + sample - 1) / sample > ThumbMaxEdge * 2) sample *= 2
        val intermediate = BitmapFactory.decodeFile(file.absolutePath, BitmapFactory.Options().apply {
            inSampleSize = sample
            inPreferredConfig = Bitmap.Config.ARGB_8888
            inScaled = false
        }) ?: return null
        val decodedEdge = maxOf(intermediate.width, intermediate.height)
        if (decodedEdge > ThumbMaxEdge * 2) {
            intermediate.recycle()
            return null
        }
        if (decodedEdge <= maxEdge) return intermediate
        return try {
            val scale = maxEdge.toDouble() / decodedEdge
            Bitmap.createScaledBitmap(intermediate,
                (intermediate.width * scale).toInt().coerceAtLeast(1),
                (intermediate.height * scale).toInt().coerceAtLeast(1), true)
        } finally {
            // Only the private intermediate is recycled, never a published thumbnail.
            intermediate.recycle()
        }
    }

    fun routePolylines(route: Route?, building: String): List<StackPolyline> {
        if (route == null) return emptyList()
        val lines = ArrayList<StackPolyline>()
        val floors = LinkedHashSet<Int>()
        for (leg in route.legs) {
            if (leg.kind == "walk" || leg.kind == "building_link") floors.add(leg.floor)
        }
        for (floor in floors) {
            for (stroke in MapsComposer.floorPathStrokes(route, building, floor)) {
                if (stroke.size < 2) continue
                val pts = stroke.map { (x, y) -> StackPoint3(x.toDouble(), y.toDouble(), floor.toDouble()) }
                lines.add(StackPolyline("walk", floor, null, pts))
            }
        }
        for (leg in route.legs) {
            if (leg.kind != "stair_up" && leg.kind != "stair_down") continue
            if (leg.building != building) continue
            if (leg.points.isEmpty()) continue
            val from = leg.points.first()
            val to = leg.points.last()
            val z0 = leg.floor
            val z1 = leg.toFloor ?: leg.floor
            lines.add(
                StackPolyline(
                    leg.kind,
                    z0,
                    z1,
                    listOf(
                        StackPoint3(from.x, from.y, z0.toDouble()),
                        StackPoint3(to.x, to.y, z1.toDouble())
                    )
                )
            )
        }
        return lines
    }

    fun project(
        route: Route?,
        building: String,
        width: Float,
        height: Float,
        yaw: Float = DefaultYaw,
        pitch: Float = DefaultPitch,
        floors: List<Int> = MapsComposer.floors(building)
    ): StackScene {
        val zMid = zMid(floors)
        val quads = floors.map { z ->
            val pts3 = listOf(
                StackPoint3(0.0, 0.0, z.toDouble()),
                StackPoint3(1.0, 0.0, z.toDouble()),
                StackPoint3(1.0, 1.0, z.toDouble()),
                StackPoint3(0.0, 1.0, z.toDouble())
            )
            StackQuad(z, pts3, pts3.map { mapPoint(it, width, height, yaw, pitch, zMid) })
        }
        val projected = routePolylines(route, building).map { line ->
            line.copy(points2 = line.points.map { mapPoint(it, width, height, yaw, pitch, zMid) })
        }
        return StackScene(quads, projected)
    }

    fun paintSequence(scene: StackScene): List<StackPaintItem> {
        val items = ArrayList<StackPaintItem>()
        for (quad in scene.floors) {
            items.add(StackPaintItem("floor", quad.floor, quad = quad))
            for (line in scene.polylines) {
                if (line.kind == "walk" && line.floor == quad.floor)
                    items.add(StackPaintItem("walk", line.floor, line = line))
            }
            for (line in scene.polylines) {
                if ((line.kind == "stair_up" || line.kind == "stair_down") && line.floor == quad.floor)
                    items.add(StackPaintItem(line.kind, line.floor, line = line))
            }
        }
        return items
    }

    fun project(
        presentation: RoutePresentation,
        building: String,
        width: Float,
        height: Float,
        yaw: Float = DefaultYaw,
        pitch: Float = DefaultPitch,
        floors: List<Int> = MapsComposer.floors(building)
    ): StackScene {
        val base = project(null as Route?, building, width, height, yaw, pitch, floors)
        val middle = zMid(floors)
        fun local(key: FloorKey) = key.building == building && key.floor in floors
        fun valid(point: ru.bgtu_voenmeh.zapara.data.campus.GraphPoint) =
            point.x.isFinite() && point.y.isFinite() && point.x in 0.0..1.0 && point.y in 0.0..1.0
        val lines = presentation.segments.mapNotNull { segment ->
            if (!local(segment.floor) || segment.kind !in listOf(RoutePartKind.Walk, RoutePartKind.BuildingLink) ||
                segment.points.size < 2 || segment.points.any { !valid(it) } ||
                RouteProblem.InvalidGeometry in segment.problems || RouteProblem.MissingCoordinates in segment.problems) null
            else StackPolyline("walk", segment.floor.floor, null,
                segment.points.map { StackPoint3(it.x, it.y, segment.floor.floor.toDouble()) })
        }.toMutableList()
        presentation.steps.forEach { step ->
            if (step.kind != RoutePartKind.StairUp && step.kind != RoutePartKind.StairDown) return@forEach
            if (!local(step.from) || !local(step.to)) return@forEach
            val from = step.markers.singleOrNull { it.kind == RouteMarkerKind.StairDeparture && it.floor == step.from && it.target == step.to }
            val to = step.markers.singleOrNull { it.kind == RouteMarkerKind.StairArrival && it.floor == step.to && it.target == step.from }
            if (from == null || to == null || !valid(from.point) || !valid(to.point)) return@forEach
            // Only a known common shaft can be connected; other local markers remain visible.
            if (from.point != to.point) return@forEach
            lines.add(StackPolyline(if (step.kind == RoutePartKind.StairUp) "stair_up" else "stair_down",
                step.from.floor, step.to.floor, listOf(
                    StackPoint3(from.point.x, from.point.y, step.from.floor.toDouble()),
                    StackPoint3(to.point.x, to.point.y, step.to.floor.toDouble()))))
        }
        val markers = (presentation.endpoints + presentation.steps.flatMap { it.markers })
            .filter { local(it.floor) && valid(it.point) }.distinct().map { marker ->
                StackMarker(marker, mapPoint(StackPoint3(marker.point.x, marker.point.y, marker.floor.floor.toDouble()),
                    width, height, yaw, pitch, middle))
            }
        return base.copy(polylines = lines.map { line -> line.copy(points2 = line.points.map {
            mapPoint(it, width, height, yaw, pitch, middle)
        }) }, markers = markers)
    }

    private fun mapPoint(
        p: StackPoint3,
        width: Float,
        height: Float,
        yaw: Float,
        pitch: Float,
        zMid: Double
    ): StackPoint2 {
        val cx = p.x - 0.5
        val cy = p.y - 0.5
        val cz = (p.z - zMid) * FloorGap
        val cosY = cos(yaw.toDouble())
        val sinY = sin(yaw.toDouble())
        val rx = cx * cosY - cy * sinY
        val ry = cx * sinY + cy * cosY
        val sy = ry * cos(pitch.toDouble()) - cz * sin(pitch.toDouble())
        val scale = min(width, height) * 0.72
        return StackPoint2(width * 0.5 + rx * scale, height * 0.5 + sy * scale)
    }

    private fun zMid(floors: List<Int>): Double {
        if (floors.isEmpty()) return 1.0
        return (floors.minOf { it } + floors.maxOf { it }) / 2.0
    }
}
