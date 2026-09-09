package ru.bgtu_voenmeh.zapara.ui.maps

import android.graphics.Bitmap
import android.graphics.BitmapFactory
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
import ru.bgtu_voenmeh.zapara.ui.theme.rememberPulse
import java.io.File

@Composable
fun ZoomableMap(
    file: File?,
    highlight: HighlightUi?,
    zoom: Float,
    onTransform: (Float) -> Unit,
    modifier: Modifier = Modifier
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
    val glow by rememberPulse(highlight != null, Durations.map, 0.4f, 0.8f)
    val bitmap by produceState<Bitmap?>(null, file?.absolutePath) {
        value = withContext(Dispatchers.IO) {
            try {
                file?.takeIf { it.exists() }?.let { BitmapFactory.decodeFile(it.absolutePath) }
            } catch (e: Exception) {
                android.util.Log.w("ZaparaMaps", "decode", e)
                null
            }
        }
    }
    val c = Zapara.colors
    val density = LocalDensity.current
    var layout by remember { mutableStateOf(IntSize.Zero) }
    Box(
        modifier
            .fillMaxSize()
            .clipToBounds()
            .onSizeChanged { layout = it }
            .testTag("Maps.Plan")
            .pointerInput(Unit) {
                detectTapGestures(onDoubleTap = {
                    gesture = false
                    scale = 1f
                    lastEmitted = 1f
                    offset = Offset.Zero
                    onTransform(1f)
                })
            }
            .transformable(transform),
        contentAlignment = Alignment.Center
    ) {
        val bmp = bitmap
        if (bmp != null) {
            Image(
                bitmap = bmp.asImageBitmap(),
                contentDescription = null,
                contentScale = ContentScale.Fit,
                modifier = Modifier.fillMaxSize().graphicsLayer(scaleX = shown, scaleY = shown, translationX = offset.x, translationY = offset.y)
            )
            if (highlight != null && layout.width > 0 && layout.height > 0) {
                val px = HighlightGeometry.mapped(
                    highlight.rect,
                    layout.width.toFloat(),
                    layout.height.toFloat(),
                    bmp.width.toFloat(),
                    bmp.height.toFloat(),
                    shown,
                    offset.x,
                    offset.y
                )
                Canvas(Modifier.fillMaxSize()) {
                    val w = px.right - px.left
                    val h = px.bottom - px.top
                    val fill = if (motion.enabled) c.mapInkSoft.copy(alpha = glow) else c.mapInkSoft
                    drawRoundRect(fill, Offset(px.left, px.top), Size(w, h), CornerRadius(4.dp.toPx()))
                    drawRoundRect(c.mapInk, Offset(px.left, px.top), Size(w, h), CornerRadius(4.dp.toPx()), style = Stroke(2.dp.toPx()))
                }
                val gapPx = with(density) { 26.dp.toPx() }
                val (chipX, chipY) = HighlightGeometry.chipOffset(px, gapPx)
                Box(
                    Modifier
                        .align(Alignment.TopStart)
                        .offset { IntOffset(chipX.roundToInt(), chipY.roundToInt()) }
                        .background(c.mapInk, RoundedCornerShape(Zapara.radii.chip))
                        .padding(horizontal = Zapara.space.s, vertical = Zapara.space.xs)
                        .testTag("Maps.Highlight")
                ) {
                    Text(highlight.label, style = Zapara.typography.caption, color = c.onMapInk)
                }
            }
        }
    }
}
