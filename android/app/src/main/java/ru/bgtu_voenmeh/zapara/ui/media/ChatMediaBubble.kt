package ru.bgtu_voenmeh.zapara.ui.media

import android.graphics.BitmapFactory
import android.graphics.Matrix
import android.graphics.SurfaceTexture
import android.media.MediaPlayer
import android.view.Surface
import android.view.TextureView
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.LocalContentColor
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.R
import java.io.File

/** Downloads stay with the chat's authenticated data layer; this view sees only private local files. */
@Composable
fun ChatMediaBubble(
    kind: String,
    file: File?,
    durationMs: Int?,
    loading: Boolean,
    error: Boolean,
    onLoad: () -> Unit,
    modifier: Modifier = Modifier
) {
    if (kind !in setOf("image", "voice", "circle")) return
    val contentColor = LocalContentColor.current
    LaunchedEffect(file?.absolutePath, error) {
        if (file == null && !error) onLoad()
    }
    Box(modifier) {
        when {
            error -> TextButton(onClick = onLoad, colors = ButtonDefaults.textButtonColors(contentColor = contentColor)) { Text(stringResource(R.string.chat_media_load_failed)) }
            loading || file == null -> Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                CircularProgressIndicator(Modifier.size(18.dp), color = contentColor, strokeWidth = 2.dp)
                Text(stringResource(when(kind) { "image" -> R.string.chat_media_loading_image; "voice" -> R.string.chat_media_loading_voice; else -> R.string.chat_media_loading_circle }), color = contentColor.copy(alpha = 0.72f))
            }
            kind == "image" -> ImagePreview(file)
            else -> Playback(kind, file, durationMs)
        }
    }
}

@Composable
private fun ImagePreview(file: File) {
    val decoded by produceState<Pair<Boolean, android.graphics.Bitmap?>>(false to null, file.absolutePath) {
        val bitmap = withContext(Dispatchers.IO) {
            try {
                val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
                BitmapFactory.decodeFile(file.absolutePath, bounds)
                if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return@withContext null
                var sample = 1
                while (bounds.outWidth / sample > 1024 || bounds.outHeight / sample > 1024) sample *= 2
                BitmapFactory.decodeFile(file.absolutePath, BitmapFactory.Options().apply { inSampleSize = sample })
            } catch (_: Exception) { null }
            catch (_: OutOfMemoryError) { null }
        }
        value = true to bitmap
    }
    val preview = decoded.second
    if (!decoded.first) CircularProgressIndicator(Modifier.size(18.dp), color = LocalContentColor.current, strokeWidth = 2.dp)
    else if (preview == null) Text(stringResource(R.string.chat_media_image_unavailable), color = LocalContentColor.current.copy(alpha = 0.72f))
    else Image(preview.asImageBitmap(), stringResource(R.string.chat_media_photo), Modifier.width(240.dp).height(180.dp).clip(RoundedCornerShape(10.dp)).testTag("Chat.ImagePreview"), contentScale = ContentScale.Crop)
}

@Composable
private fun Playback(kind: String, file: File, suppliedDuration: Int?) {
    val contentColor = LocalContentColor.current
    val controller = remember(file.absolutePath) { LocalMediaPlayer(file) }
    DisposableEffect(controller) {
        controller.prepare()
        onDispose { controller.close() }
    }
    LaunchedEffect(controller, controller.playing) {
        while (controller.playing) {
            controller.updatePosition()
            delay(200)
        }
    }
    val duration = controller.durationMs.takeIf { it > 0 } ?: suppliedDuration?.coerceAtLeast(0) ?: 0
    if (controller.failed) {
        Text(stringResource(R.string.chat_media_play_failed), color = contentColor.copy(alpha = 0.72f))
        return
    }
    if (kind == "voice") {
        Column {
            Row(verticalAlignment = Alignment.CenterVertically) {
                TextButton(onClick = { controller.toggle() }, enabled = controller.ready,
                    colors = ButtonDefaults.textButtonColors(contentColor = contentColor)) { Text(if (controller.playing) "Ⅱ" else "▶") }
                Text("${chatClock(controller.positionMs)} / ${chatClock(duration)}", color = contentColor, style = Zapara.typography.caption)
            }
            Slider(value = if (duration > 0) controller.positionMs.toFloat() / duration else 0f,
                onValueChange = { controller.seek((it * duration).toInt()) }, enabled = controller.ready && duration > 0,
                colors = SliderDefaults.colors(thumbColor = contentColor, activeTrackColor = contentColor, inactiveTrackColor = contentColor.copy(alpha = 0.28f)),
                modifier = Modifier.width(210.dp).height(24.dp))
        }
    } else {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Box(Modifier.size(212.dp).clip(CircleShape).background(Zapara.colors.chip), contentAlignment = Alignment.Center) {
                AndroidView(factory = { context -> TextureView(context).apply {
                    surfaceTextureListener = object : TextureView.SurfaceTextureListener {
                        override fun onSurfaceTextureAvailable(surface: SurfaceTexture, width: Int, height: Int) { controller.attach(Surface(surface)); centerCrop(this@apply, controller.videoWidth, controller.videoHeight) }
                        override fun onSurfaceTextureSizeChanged(surface: SurfaceTexture, width: Int, height: Int) { centerCrop(this@apply, controller.videoWidth, controller.videoHeight) }
                        override fun onSurfaceTextureDestroyed(surface: SurfaceTexture): Boolean { controller.detach(); return true }
                        override fun onSurfaceTextureUpdated(surface: SurfaceTexture) = Unit
                    }
                } }, update = { centerCrop(it, controller.videoWidth, controller.videoHeight) }, modifier = Modifier.fillMaxSize())
                Box(Modifier.fillMaxSize().clickable { controller.toggle() }.testTag("Chat.CirclePlayback"), contentAlignment = Alignment.Center) {
                    if (!controller.playing) Text("▶", color = Zapara.colors.qrPaper, style = Zapara.typography.title)
                }
            }
            Text("${chatClock(controller.positionMs)} / ${chatClock(duration)}", color = contentColor.copy(alpha = 0.72f), style = Zapara.typography.caption)
            Slider(value = if (duration > 0) controller.positionMs.toFloat() / duration else 0f,
                onValueChange = { controller.seek((it * duration).toInt()) }, enabled = controller.ready && duration > 0,
                colors = SliderDefaults.colors(thumbColor = contentColor, activeTrackColor = contentColor, inactiveTrackColor = contentColor.copy(alpha = 0.28f)),
                modifier = Modifier.width(190.dp).height(24.dp))
        }
    }
}

private fun centerCrop(view: TextureView, videoWidth: Int, videoHeight: Int) {
    if (view.width == 0 || view.height == 0 || videoWidth == 0 || videoHeight == 0) return
    val sourceRatio = videoWidth.toFloat() / videoHeight
    val targetRatio = view.width.toFloat() / view.height
    val scaleX = if (sourceRatio > targetRatio) sourceRatio / targetRatio else 1f
    val scaleY = if (sourceRatio < targetRatio) targetRatio / sourceRatio else 1f
    view.setTransform(Matrix().apply { setScale(scaleX, scaleY, view.width / 2f, view.height / 2f) })
}

private class LocalMediaPlayer(private val file: File) {
    var ready by mutableStateOf(false)
        private set
    var failed by mutableStateOf(false)
        private set
    var playing by mutableStateOf(false)
        private set
    var durationMs by mutableIntStateOf(0)
        private set
    var positionMs by mutableIntStateOf(0)
        private set
    var videoWidth by mutableIntStateOf(0)
        private set
    var videoHeight by mutableIntStateOf(0)
        private set
    private var player: MediaPlayer? = null
    private var surface: Surface? = null

    fun prepare() {
        if (player != null) return
        try {
            val media = MediaPlayer()
            player = media
            media.setOnPreparedListener {
                ready = true
                durationMs = it.duration.coerceAtLeast(0)
            }
            media.setOnCompletionListener { playing = false; positionMs = 0; it.seekTo(0) }
            media.setOnErrorListener { _, _, _ -> failed = true; playing = false; true }
            media.setOnVideoSizeChangedListener { _, width, height -> videoWidth = width; videoHeight = height }
            media.setDataSource(file.absolutePath)
            surface?.let(media::setSurface)
            media.prepareAsync()
        } catch (_: Exception) { failed = true; close() }
    }

    fun toggle() {
        val media = player ?: return
        if (!ready) return
        try {
            if (media.isPlaying) media.pause() else media.start()
            playing = media.isPlaying
        } catch (_: Exception) { failed = true; playing = false }
    }

    fun seek(ms: Int) {
        if (!ready) return
        try { player?.seekTo(ms.coerceIn(0, durationMs)); positionMs = ms.coerceIn(0, durationMs) }
        catch (_: Exception) { failed = true }
    }

    fun updatePosition() {
        try { positionMs = player?.currentPosition?.coerceAtLeast(0) ?: 0 }
        catch (_: Exception) { playing = false }
    }

    fun attach(next: Surface) {
        surface?.release()
        surface = next
        player?.setSurface(next)
    }

    fun detach() {
        player?.setSurface(null)
        surface?.release()
        surface = null
    }

    fun close() {
        playing = false
        ready = false
        player?.release()
        player = null
        surface?.release()
        surface = null
    }
}
