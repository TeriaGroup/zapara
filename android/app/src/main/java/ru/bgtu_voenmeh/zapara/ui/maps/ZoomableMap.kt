package ru.bgtu_voenmeh.zapara.ui.maps

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import androidx.compose.animation.core.animate
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.rememberTransformableState
import androidx.compose.foundation.gestures.transformable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.sizeIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.dp
import kotlin.math.roundToInt
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.ui.theme.Durations
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaEase
import ru.bgtu_voenmeh.zapara.ui.theme.rememberMarch
import ru.bgtu_voenmeh.zapara.ui.theme.rememberPulse
import java.io.File

@Composable
fun ZoomableMap(
    file: File?,
    highlight: HighlightUi?,
    zoom: Float,
    onTransform: (Float) -> Unit,
    modifier: Modifier = Modifier,
    path: List<List<Pair<Float, Float>>>? = null,
    stairMarkers: List<StairMarkerUi> = emptyList(),
    fitGeneration: Int = 0,
    onLongPress: ((Double, Double) -> Unit)? = null,
    presentation: RoutePresentation? = null,
    floorKey: FloorKey? = null,
    activeStepId: Int? = null,
    onMapUnavailable: (() -> Unit)? = null
) {
    val motion = Zapara.motion
    var gesture by remember { mutableStateOf(false) }
    var scale by remember { mutableFloatStateOf(zoom) }
    var lastEmitted by remember { mutableFloatStateOf(zoom) }
    val animatedZoom by animateFloatAsState(targetValue = zoom, animationSpec = tween(motion.ms(Durations.zoom), easing = ZaparaEase), label = "mapZoom")
    val shown = MapZoom.shown(gesture, scale, animatedZoom)
    var offset by remember { mutableStateOf(Offset.Zero) }
    val transform = rememberTransformableState { zoomChange, panChange, _ ->
        val next = MapZoom.pinch(shown, zoomChange)
        gesture = true
        scale = next
        lastEmitted = next
        offset += panChange
        onTransform(next)
    }
    LaunchedEffect(zoom) {
        if (MapZoom.isButtonZoom(zoom, lastEmitted)) {
            gesture = false
            scale = zoom
            lastEmitted = zoom
        }
    }
    LaunchedEffect(fitGeneration) {
        if (fitGeneration == 0) return@LaunchedEffect
        gesture = false
        scale = 1f
        lastEmitted = 1f
        offset = Offset.Zero
        onTransform(1f)
    }
    val glow by rememberPulse(highlight != null, Durations.map, 0.4f, 0.8f)
    var drawProgress by remember(path, motion.enabled) { mutableFloatStateOf(if (motion.enabled && !path.isNullOrEmpty()) 0f else 1f) }
    LaunchedEffect(path, motion.enabled) {
        if (path.isNullOrEmpty() || !motion.enabled) {
            drawProgress = 1f
            return@LaunchedEffect
        }
        drawProgress = 0f
        animate(0f, 1f, animationSpec = tween(motion.ms(Durations.map), easing = ZaparaEase)) { value, _ ->
            drawProgress = value
        }
    }
    val march by rememberMarch(presentation == null && motion.enabled && !path.isNullOrEmpty() && drawProgress >= 1f, Durations.map)
    val unavailableCallback by androidx.compose.runtime.rememberUpdatedState(onMapUnavailable)
    val bitmap by produceState<Bitmap?>(null, file?.absolutePath, floorKey) {
        value = null
        value = withContext(Dispatchers.IO) {
            try {
                file?.takeIf { it.exists() }?.let { BitmapFactory.decodeFile(it.absolutePath) }
            } catch (e: Exception) {
                android.util.Log.w("ZaparaMaps", "decode", e)
                null
            }
        }
        if (value == null && file != null) unavailableCallback?.invoke()
    }
    val c = Zapara.colors
    val density = LocalDensity.current
    var layout by remember { mutableStateOf(IntSize.Zero) }
    var prevSize by remember { mutableStateOf(IntSize.Zero) }
    val press = remember { PlanPressState() }
    press.shown = shown
    press.offset = offset
    press.layout = layout
    press.bitmap = bitmap
    press.onLongPress = onLongPress
    Box(
        modifier
            .fillMaxSize()
            .clipToBounds()
            .onSizeChanged { new ->
                if (MapZoom.shouldResetView(prevSize.width, prevSize.height, new.width, new.height)) {
                    gesture = false
                    scale = 1f
                    lastEmitted = 1f
                    offset = Offset.Zero
                    onTransform(1f)
                }
                prevSize = new
                layout = new
            }
            .testTag("Maps.Plan")
            .pointerInput(Unit) {
                detectTapGestures(
                    onDoubleTap = {
                        gesture = false
                        scale = 1f
                        lastEmitted = 1f
                        offset = Offset.Zero
                        onTransform(1f)
                    },
                    onLongPress = { tap ->
                        val bmp = press.bitmap ?: return@detectTapGestures
                        val size = press.layout
                        if (size.width <= 0 || size.height <= 0) return@detectTapGestures
                        val n = HighlightGeometry.fromLayout(
                            tap.x, tap.y,
                            size.width.toFloat(), size.height.toFloat(),
                            bmp.width.toFloat(), bmp.height.toFloat(),
                            press.shown, press.offset.x, press.offset.y
                        ) ?: return@detectTapGestures
                        press.onLongPress?.invoke(n.first.toDouble(), n.second.toDouble())
                    }
                )
            }
            .transformable(transform),
        contentAlignment = Alignment.Center
    ) {
        val bmp = bitmap
        if (bmp != null) {
            Box(Modifier.fillMaxSize().graphicsLayer(scaleX = shown, scaleY = shown, translationX = offset.x, translationY = offset.y)) {
            Image(
                bitmap = bmp.asImageBitmap(),
                contentDescription = null,
                contentScale = ContentScale.Fit,
                modifier = Modifier.fillMaxSize()
            )
            if (presentation != null && floorKey != null && layout.width > 0 && layout.height > 0) {
                RouteMapOverlay(presentation, floorKey, activeStepId, HighlightGeometry.fit(
                    layout.width.toFloat(), layout.height.toFloat(), bmp.width.toFloat(), bmp.height.toFloat()),
                    visibleBounds = HighlightGeometry.ChipBox(
                        layout.width / 2f + (-offset.x - layout.width / 2f) / shown,
                        layout.height / 2f + (-offset.y - layout.height / 2f) / shown,
                        layout.width / shown, layout.height / shown))
            }
            }
            if (layout.width > 0 && layout.height > 0) {
                val mappedPath = PathGeometry.mapStrokes(
                    if (presentation == null) path else null,
                    layout.width.toFloat(),
                    layout.height.toFloat(),
                    bmp.width.toFloat(),
                    bmp.height.toFloat(),
                    shown,
                    offset.x,
                    offset.y
                )
                val px = highlight?.let {
                    HighlightGeometry.mapped(
                        it.rect,
                        layout.width.toFloat(),
                        layout.height.toFloat(),
                        bmp.width.toFloat(),
                        bmp.height.toFloat(),
                        shown,
                        offset.x,
                        offset.y
                    )
                }
                val markerAnchors = (if (presentation == null) stairMarkers else emptyList()).map { marker ->
                    marker to PathGeometry.mapped(marker.x.toFloat(), marker.y.toFloat(),
                        layout.width.toFloat(), layout.height.toFloat(), bmp.width.toFloat(), bmp.height.toFloat(),
                        shown, offset.x, offset.y)
                }.filter { (_, p) -> p.x in 0f..layout.width.toFloat() && p.y in 0f..layout.height.toFloat() }
                if (mappedPath.isNotEmpty() || px != null || markerAnchors.isNotEmpty()) {
                    Canvas(Modifier.fillMaxSize()) {
                        fun polyline(points: List<PathPx>): Path {
                            val line = Path()
                            line.moveTo(points[0].x, points[0].y)
                            for (i in 1 until points.size) line.lineTo(points[i].x, points[i].y)
                            return line
                        }
                        val frame = PathGeometry.at(mappedPath, if (motion.enabled) drawProgress else 1f)
                        mappedPath.forEach { stroke ->
                            drawPath(
                                polyline(stroke),
                                c.mapInkSoft,
                                style = Stroke(5.dp.toPx(), cap = StrokeCap.Round, join = StrokeJoin.Round)
                            )
                        }
                        frame.revealed.forEach { stroke ->
                            drawPath(
                                polyline(stroke),
                                c.mapInk,
                                style = Stroke(4.dp.toPx(), cap = StrokeCap.Round, join = StrokeJoin.Round)
                            )
                        }
                        if (frame.complete && motion.enabled) {
                            val phase = PathGeometry.dashOffset(march * Durations.map, Durations.map, 18f)
                            val dash = PathEffect.dashPathEffect(floatArrayOf(10.dp.toPx(), 8.dp.toPx()), phase)
                            mappedPath.forEach { stroke ->
                                drawPath(
                                    polyline(stroke),
                                    c.onMapInk,
                                    style = Stroke(2.dp.toPx(), cap = StrokeCap.Round, join = StrokeJoin.Round, pathEffect = dash)
                                )
                            }
                        }
                        frame.start?.let {
                            drawCircle(c.mapInk, 5.dp.toPx(), Offset(it.x, it.y), style = Stroke(2.dp.toPx()))
                        }
                        frame.end?.let {
                            drawCircle(c.mapInk, 6.dp.toPx(), Offset(it.x, it.y))
                            drawCircle(c.onMapInk, 3.dp.toPx(), Offset(it.x, it.y))
                        }
                        if (motion.enabled) {
                            val head = if (frame.complete) PathGeometry.at(mappedPath, march).head else frame.head
                            head?.let {
                                drawCircle(c.onMapInk, 6.dp.toPx(), Offset(it.x, it.y))
                                drawCircle(c.mapInk, 4.dp.toPx(), Offset(it.x, it.y))
                            }
                        }
                        if (px != null) {
                            val w = px.right - px.left
                            val h = px.bottom - px.top
                            val fill = if (motion.enabled) c.mapInkSoft.copy(alpha = glow) else c.mapInkSoft
                            drawRoundRect(fill, Offset(px.left, px.top), Size(w, h), CornerRadius(4.dp.toPx()))
                            drawRoundRect(c.mapInk, Offset(px.left, px.top), Size(w, h), CornerRadius(4.dp.toPx()), style = Stroke(2.dp.toPx()))
                        }
                        markerAnchors.forEach { (_, p) ->
                            drawCircle(c.onMapInk, 7.dp.toPx(), Offset(p.x, p.y))
                            drawCircle(c.mapInk, 5.dp.toPx(), Offset(p.x, p.y))
                        }
                    }
                }
                val badgeHeight = with(density) { 30.dp.toPx() }
                val badgeWidth = with(density) { 64.dp.toPx() }
                val stairGap = with(density) { 7.dp.toPx() }
                val stairBoxes = markerAnchors.map { (_, p) ->
                    val left = (p.x + stairGap).coerceIn(0f, (layout.width - badgeWidth).coerceAtLeast(0f))
                    val top = (p.y - badgeHeight).coerceIn(0f, (layout.height - badgeHeight).coerceAtLeast(0f))
                    HighlightGeometry.ChipBox(left, top, badgeWidth, badgeHeight)
                }
                markerAnchors.forEachIndexed { i, (marker, _) ->
                    val box = stairBoxes[i]
                    Box(
                        Modifier.align(Alignment.TopStart)
                            .offset { IntOffset(box.x.roundToInt(), box.y.roundToInt()) }
                            .background(c.mapInk, RoundedCornerShape(Zapara.radii.chip))
                            .padding(horizontal = Zapara.space.s, vertical = Zapara.space.xs)
                            .testTag("Maps.StairMarker")
                    ) {
                        Text(marker.label, style = Zapara.typography.caption, color = c.onMapInk)
                    }
                }
                if (px != null && presentation == null) {
                    val gapPx = with(density) { 26.dp.toPx() }
                    val preferred = HighlightGeometry.chipOffset(px, gapPx)
                    val chipW = with(density) { (32 + (highlight?.label.orEmpty().length * 8)).dp.toPx() }
                    val (chipX, chipY) = HighlightGeometry.dodge(
                        preferred.first, preferred.second, chipW, badgeHeight,
                        stairBoxes, layout.width.toFloat(), layout.height.toFloat(), 8f
                    )
                    Box(
                        Modifier
                            .align(Alignment.TopStart)
                            .offset { IntOffset(chipX.roundToInt(), chipY.roundToInt()) }
                            .background(c.mapInk, RoundedCornerShape(Zapara.radii.chip))
                            .sizeIn(minWidth = Zapara.space.minTouch, minHeight = Zapara.space.minTouch)
                            .padding(horizontal = Zapara.space.s, vertical = Zapara.space.xs)
                            .testTag("Maps.Highlight")
                            .pointerInput(highlight) {
                                detectTapGestures(
                                    onLongPress = {
                                        val room = highlight ?: return@detectTapGestures
                                        onLongPress?.invoke(room.rect.x + room.rect.w / 2, room.rect.y + room.rect.h / 2)
                                    }
                                )
                            }
                    ) {
                        Text(highlight?.label.orEmpty(), style = Zapara.typography.caption, color = c.onMapInk)
                    }
                }
            }
        }
    }
}

private class PlanPressState {
    var shown: Float = 1f
    var offset: Offset = Offset.Zero
    var layout: IntSize = IntSize.Zero
    var bitmap: Bitmap? = null
    var onLongPress: ((Double, Double) -> Unit)? = null
}
