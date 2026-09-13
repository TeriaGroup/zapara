package ru.bgtu_voenmeh.zapara.ui.maps

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.graphics.Matrix
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.withTransform
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import java.io.File
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.data.campus.Route
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

@Composable
fun CampusStack(
    route: Route?,
    building: String,
    floors: List<Int>,
    floorFiles: Map<Int, File>,
    modifier: Modifier = Modifier,
    rasterRevision: StackRasterRevision? = null,
    presentation: RoutePresentation? = null,
    activeFloor: Int? = null,
    onFloorSelect: ((Int) -> Unit)? = null,
    onRetry: (() -> Unit)? = null
) {
    val allowOrbit = Zapara.motion.enabled
    var yaw by remember { mutableFloatStateOf(CampusStackProjector.DefaultYaw) }
    var pitch by remember { mutableFloatStateOf(CampusStackProjector.DefaultPitch) }
    val loader = remember { StackThumbnailLoader() }
    val loaded = produceThumbnails(building, floors, floorFiles, rasterRevision) {
        val bitmaps = loader.load(building, floorFiles.filterKeys { it in floors }, rasterRevision)
        val current = withContext(Dispatchers.IO) { bitmaps.keys.all { it.unchanged() } }
        currentCoroutineContext().ensureActive()
        if (current) {
            bitmaps
        } else null
    }
    // produceState retains its old value until its effect runs; gate synchronously on the request.
    val images = loaded.orEmpty().entries.associate { it.key.floor.floor to it.value.asImageBitmap() }
    val c = Zapara.colors
    val space = Zapara.space
    Column(modifier.fillMaxSize()) {
    Canvas(
        Modifier
            .fillMaxWidth().weight(1f)
            .clipToBounds()
            .testTag("Maps.StackView")
            .pointerInput(allowOrbit) {
                if (!allowOrbit) return@pointerInput
                detectDragGestures { change, amount ->
                    change.consume()
                    yaw += amount.x * 0.01f
                    pitch = (pitch - amount.y * 0.01f).coerceIn(0.08f, 1.2f)
                }
            }
    ) {
        val scene = (presentation?.let {
            CampusStackProjector.project(it, building, size.width, size.height, yaw, pitch, floors)
        } ?: CampusStackProjector.project(null as Route?, building, size.width, size.height, yaw, pitch, floors))
            .fitOverview(size.width, size.height, space.s.toPx()).withRasters(images.keys)
        fun polyline(points: List<PathPx>): Path {
            val stroke = Path()
            stroke.moveTo(points[0].x, points[0].y)
            for (i in 1 until points.size) stroke.lineTo(points[i].x, points[i].y)
            return stroke
        }
        val strokes = scene.polylines.mapNotNull { line ->
            if (line.points2.size < 2) null
            else line.points2.map { PathPx(it.x.toFloat(), it.y.toFloat()) }
        }
        val frame = PathGeometry.at(strokes, 1f)
        CampusStackProjector.paintSequence(scene).forEach { item ->
            val quad = item.quad
            if (item.kind == "floor" && quad != null && quad.points2.size >= 4) {
                val outline = Path()
                outline.moveTo(quad.points2[0].x.toFloat(), quad.points2[0].y.toFloat())
                for (i in 1 until quad.points2.size) {
                    outline.lineTo(quad.points2[i].x.toFloat(), quad.points2[i].y.toFloat())
                }
                outline.close()
                val img = images[quad.floor]
                if (img != null) {
                    val p00 = quad.points2[0]
                    val p10 = quad.points2[1]
                    val p01 = quad.points2[3]
                    val w = img.width.toFloat()
                    val h = img.height.toFloat()
                    if (w > 0f && h > 0f) {
                        val a = ((p10.x - p00.x) / w).toFloat()
                        val cc = ((p10.y - p00.y) / w).toFloat()
                        val b = ((p01.x - p00.x) / h).toFloat()
                        val d = ((p01.y - p00.y) / h).toFloat()
                        val matrix = Matrix(
                            floatArrayOf(
                                a, cc, 0f, 0f,
                                b, d, 0f, 0f,
                                0f, 0f, 1f, 0f,
                                p00.x.toFloat(), p00.y.toFloat(), 0f, 1f
                            )
                        )
                        withTransform({ transform(matrix) }) { drawImage(img) }
                    }
                } else {
                    drawPath(outline, c.card)
                }
                drawPath(outline, c.lineStrong, style = Stroke(
                    (if (quad.floor == activeFloor) space.xs else space.hairline).toPx()))
                scene.markers.filter { it.marker.floor.floor == quad.floor }.forEach { marker ->
                    val center = Offset(marker.point2.x.toFloat(), marker.point2.y.toFloat())
                    drawCircle(c.onMapInk, space.s.toPx(), center)
                    drawCircle(c.mapInk, space.xs.toPx(), center)
                }
                return@forEach
            }
            val line = item.line ?: return@forEach
            if (line.points2.size < 2) return@forEach
            val stroke = line.points2.map { PathPx(it.x.toFloat(), it.y.toFloat()) }
            drawPath(polyline(stroke), c.mapInkSoft, style = Stroke(4.dp.toPx(), cap = StrokeCap.Round, join = StrokeJoin.Round))
            drawPath(polyline(stroke), c.mapInk, style = Stroke(3.dp.toPx(), cap = StrokeCap.Round, join = StrokeJoin.Round))
        }
        if (presentation == null) frame.start?.let { drawCircle(c.mapInk, 4.dp.toPx(), Offset(it.x, it.y), style = Stroke(2.dp.toPx())) }
        if (presentation == null) frame.end?.let {
            drawCircle(c.mapInk, 5.dp.toPx(), Offset(it.x, it.y))
            drawCircle(c.onMapInk, 2.5.dp.toPx(), Offset(it.x, it.y))
        }
    }
    key(building, activeFloor) {
        CampusStackLegend(building, floors, images.keys, loaded == null, floorFiles.keys,
            presentation, activeFloor, onFloorSelect, onRetry, Modifier.fillMaxWidth().weight(1f))
    }
    }
}
