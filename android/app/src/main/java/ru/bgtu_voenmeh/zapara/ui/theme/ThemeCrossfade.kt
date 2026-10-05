package ru.bgtu_voenmeh.zapara.ui.theme

import android.util.Log
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.platform.testTag
import androidx.core.view.drawToBitmap

@Composable
fun ThemeCrossfade(key: Any, motion: MotionSettings, modifier: Modifier = Modifier, content: @Composable () -> Unit) {
    val view = LocalView.current
    val first = remember { booleanArrayOf(true) }
    val captured = remember(key) {
        if (first[0]) {
            first[0] = false
            null
        } else if (motion.enabled && view.width > 0 && view.height > 0) {
            runCatching { view.drawToBitmap().asImageBitmap() }.getOrElse {
                Log.w("zapara", "theme snapshot", it)
                null
            }
        } else null
    }
    Box(modifier) {
        content()
        captured?.let { bmp ->
            SnapshotOverlay(bmp, motion)
        }
    }
}

@Composable
private fun SnapshotOverlay(bmp: ImageBitmap, motion: MotionSettings) {
    val alpha = remember(bmp) { Animatable(1f) }
    var show by remember(bmp) { mutableStateOf(true) }
    LaunchedEffect(bmp) {
        alpha.animateTo(0f, tween(motion.ms(Durations.theme), easing = ZaparaEase))
        show = false
    }
    if (show) {
        Image(
            bitmap = bmp,
            contentDescription = null,
            modifier = Modifier
                .fillMaxSize()
                .graphicsLayer { this.alpha = alpha.value }
                .testTag("Crossfade.Snapshot"),
            contentScale = ContentScale.FillBounds
        )
    }
}
