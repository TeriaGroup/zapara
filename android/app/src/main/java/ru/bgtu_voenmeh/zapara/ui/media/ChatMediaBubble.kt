package ru.bgtu_voenmeh.zapara.ui.media

import android.graphics.BitmapFactory
import android.graphics.Matrix
import android.graphics.SurfaceTexture
import android.media.MediaPlayer
import android.media.PlaybackParams
import android.view.Surface
import android.view.TextureView
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.rememberTransformableState
import androidx.compose.foundation.gestures.transformable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.widthIn
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
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
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
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.maps.MapZoom
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
    val loadState = chatMediaLoadState(file?.isFile == true, loading, error)
    Box(modifier) {
        when (loadState) {
            ChatMediaLoadState.Retry -> TextButton(onClick = onLoad, colors = ButtonDefaults.textButtonColors(contentColor = contentColor)) { Text(stringResource(R.string.chat_media_load_failed)) }
            ChatMediaLoadState.NeedsTap -> TextButton(onClick = onLoad,
                colors = ButtonDefaults.textButtonColors(contentColor = contentColor),
                modifier = Modifier.testTag("Chat.MediaLoad")) {
                Text(stringResource(when (kind) {
                    "image" -> R.string.ux60_chat_media_load_image
                    "voice" -> R.string.ux60_chat_media_load_voice
                    else -> R.string.ux60_chat_media_load_circle
                }))
            }
            ChatMediaLoadState.Loading -> Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                CircularProgressIndicator(Modifier.size(18.dp), color = contentColor, strokeWidth = 2.dp)
                Text(stringResource(when(kind) { "image" -> R.string.chat_media_loading_image; "voice" -> R.string.chat_media_loading_voice; else -> R.string.chat_media_loading_circle }), color = contentColor.copy(alpha = 0.72f))
            }
            ChatMediaLoadState.Ready -> if (kind == "image") ImagePreview(file!!) else Playback(kind, file!!, durationMs)
        }
    }
}

@Composable
private fun ImagePreview(file: File) {
    var attempt by remember(file.absolutePath) { mutableIntStateOf(0) }
    val decoded by produceState<Pair<Boolean, android.graphics.Bitmap?>>(false to null, file.absolutePath, attempt) {
        value = false to null
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
    else if (preview == null) Column {
        Text(stringResource(R.string.chat_media_image_unavailable), color = LocalContentColor.current.copy(alpha = 0.72f))
        TextButton(onClick = { attempt++ }, modifier = Modifier.heightIn(min = 48.dp)) {
            Text(stringResource(R.string.ux100_chat_image_retry))
        }
    }
    else {
        var viewerOpen by remember(file.absolutePath) { mutableStateOf(false) }
        Image(preview.asImageBitmap(), stringResource(R.string.chat_media_photo),
            Modifier.width(240.dp).height(180.dp).clip(RoundedCornerShape(10.dp))
                .clickable { viewerOpen = true }.testTag("Chat.ImagePreview"), contentScale = ContentScale.Crop)
        if (viewerOpen) ChatPhotoViewer(preview) { viewerOpen = false }
    }
}

@Composable
private fun ChatPhotoViewer(bitmap: android.graphics.Bitmap, onClose: () -> Unit) {
    var viewport by remember(bitmap) { mutableStateOf(IntSize.Zero) }
    var scale by remember(bitmap) { mutableStateOf(1f) }
    var pan by remember(bitmap) { mutableStateOf(Offset.Zero) }
    fun setZoom(next: Float) {
        scale = next.coerceIn(1f, MapZoom.Max)
        val bounded = MapZoom.clampPan(pan.x, pan.y, viewport.width.toFloat(), viewport.height.toFloat(),
            bitmap.width.toFloat(), bitmap.height.toFloat(), scale)
        pan = Offset(bounded.first, bounded.second)
    }
    val transform = rememberTransformableState { zoomChange, panChange, _ ->
        val nextScale = (scale * zoomChange).coerceIn(1f, MapZoom.Max)
        val bounded = MapZoom.clampPan(pan.x + panChange.x, pan.y + panChange.y,
            viewport.width.toFloat(), viewport.height.toFloat(), bitmap.width.toFloat(), bitmap.height.toFloat(), nextScale)
        scale = nextScale
        pan = Offset(bounded.first, bounded.second)
    }
    Dialog(onDismissRequest = onClose, properties = DialogProperties(usePlatformDefaultWidth = false)) {
        Box(Modifier.fillMaxSize().background(Color.Black).testTag("Chat.PhotoViewer")) {
            Image(bitmap.asImageBitmap(), stringResource(R.string.chat_media_photo),
                Modifier.fillMaxSize().clipToBounds().onSizeChanged { size ->
                    viewport = size
                    val bounded = MapZoom.clampPan(pan.x, pan.y,
                        size.width.toFloat(), size.height.toFloat(), bitmap.width.toFloat(), bitmap.height.toFloat(), scale)
                    pan = Offset(bounded.first, bounded.second)
                }
                    .pointerInput(bitmap) { detectTapGestures(onDoubleTap = { scale = 1f; pan = Offset.Zero }) }
                    .transformable(transform)
                    .graphicsLayer(scaleX = scale, scaleY = scale, translationX = pan.x, translationY = pan.y),
                contentScale = ContentScale.Fit)
            TextButton(onClick = onClose,
                modifier = Modifier.align(Alignment.TopEnd).padding(12.dp),
                colors = ButtonDefaults.textButtonColors(contentColor = Color.White)) {
                Text(stringResource(R.string.ux60_chat_photo_close))
            }
            Row(Modifier.align(Alignment.BottomCenter).padding(16.dp)
                .background(Color.Black.copy(alpha = 0.8f), RoundedCornerShape(12.dp)),
                verticalAlignment = Alignment.CenterVertically) {
                val zoomOut = stringResource(R.string.ux30_chat_zoom_out)
                val zoomIn = stringResource(R.string.ux30_chat_zoom_in)
                val reset = stringResource(R.string.ux30_chat_zoom_reset)
                TextButton(onClick = { setZoom(scale / 1.5f) }, enabled = scale > 1f,
                    modifier = Modifier.heightIn(min = 48.dp).widthIn(min = 48.dp).testTag("Chat.PhotoZoomOut")
                        .semantics { contentDescription = zoomOut },
                    colors = ButtonDefaults.textButtonColors(contentColor = Color.White)) { Text("−") }
                TextButton(onClick = { scale = 1f; pan = Offset.Zero },
                    modifier = Modifier.heightIn(min = 48.dp).testTag("Chat.PhotoZoomReset")
                        .semantics { contentDescription = reset },
                    colors = ButtonDefaults.textButtonColors(contentColor = Color.White)) { Text("${(scale * 100).toInt()}%") }
                TextButton(onClick = { setZoom(scale * 1.5f) }, enabled = scale < MapZoom.Max,
                    modifier = Modifier.heightIn(min = 48.dp).widthIn(min = 48.dp).testTag("Chat.PhotoZoomIn")
                        .semantics { contentDescription = zoomIn },
                    colors = ButtonDefaults.textButtonColors(contentColor = Color.White)) { Text("+") }
            }
        }
    }
}

@Composable
private fun Playback(kind: String, file: File, suppliedDuration: Int?) {
    val contentColor = LocalContentColor.current
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    var attempt by remember(file.absolutePath) { mutableIntStateOf(0) }
    val controller = remember(file.absolutePath, attempt) { LocalMediaPlayer(file) }
    DisposableEffect(controller, lifecycle) {
        controller.prepare()
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_STOP) controller.pause()
        }
        lifecycle.addObserver(observer)
        onDispose { lifecycle.removeObserver(observer); controller.close() }
    }
    LaunchedEffect(controller, controller.playing) {
        while (controller.playing) {
            controller.updatePosition()
            delay(200)
        }
    }
    val duration = controller.durationMs.takeIf { it > 0 } ?: suppliedDuration?.coerceAtLeast(0) ?: 0
    if (controller.failed) {
        Column {
            Text(stringResource(R.string.chat_media_play_failed), color = contentColor.copy(alpha = 0.72f))
            TextButton(onClick = { attempt++ }, modifier = Modifier.heightIn(min = 48.dp),
                colors = ButtonDefaults.textButtonColors(contentColor = contentColor)) {
                Text(stringResource(R.string.ux100_chat_player_retry))
            }
        }
        return
    }
    val seekDescription = stringResource(R.string.ux100_chat_playback_seek)
    if (kind == "voice") {
        Column {
            Row(verticalAlignment = Alignment.CenterVertically) {
                val playbackLabel = stringResource(if (controller.playing) R.string.ux30_chat_pause else R.string.ux30_chat_play)
                TextButton(onClick = { controller.toggle() }, enabled = controller.ready,
                    modifier = Modifier.heightIn(min = 48.dp).semantics { contentDescription = playbackLabel },
                    colors = ButtonDefaults.textButtonColors(contentColor = contentColor)) { Text(if (controller.playing) "Ⅱ" else "▶") }
                Text("${chatClock(controller.positionMs)} / ${chatClock(duration)}", color = contentColor, style = Zapara.typography.caption)
            }
            Slider(value = if (duration > 0) controller.positionMs.toFloat() / duration else 0f,
                onValueChange = { controller.seek((it * duration).toInt()) }, enabled = controller.ready && duration > 0,
                colors = SliderDefaults.colors(thumbColor = contentColor, activeTrackColor = contentColor, inactiveTrackColor = contentColor.copy(alpha = 0.28f)),
                modifier = Modifier.width(210.dp).heightIn(min = 48.dp).semantics { contentDescription = seekDescription })
            PlaybackSeekControls(controller, duration, contentColor)
            PlaybackSpeedSelector(controller, contentColor)
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
                val playbackLabel = stringResource(if (controller.playing) R.string.ux30_chat_pause else R.string.ux30_chat_play)
                Box(Modifier.fillMaxSize().clickable(onClickLabel = playbackLabel) { controller.toggle() }
                    .semantics { contentDescription = playbackLabel }.testTag("Chat.CirclePlayback"), contentAlignment = Alignment.Center) {
                    if (!controller.playing) Box(Modifier.size(48.dp).clip(CircleShape)
                        .background(Color.Black.copy(alpha = 0.68f)), contentAlignment = Alignment.Center) {
                        Text("▶", color = Color.White, style = Zapara.typography.title)
                    }
                }
            }
            Text("${chatClock(controller.positionMs)} / ${chatClock(duration)}", color = contentColor.copy(alpha = 0.72f), style = Zapara.typography.caption)
            Slider(value = if (duration > 0) controller.positionMs.toFloat() / duration else 0f,
                onValueChange = { controller.seek((it * duration).toInt()) }, enabled = controller.ready && duration > 0,
                colors = SliderDefaults.colors(thumbColor = contentColor, activeTrackColor = contentColor, inactiveTrackColor = contentColor.copy(alpha = 0.28f)),
                modifier = Modifier.width(190.dp).heightIn(min = 48.dp).semantics { contentDescription = seekDescription })
            PlaybackSeekControls(controller, duration, contentColor)
            PlaybackSpeedSelector(controller, contentColor)
        }
    }
}

@Composable
private fun PlaybackSeekControls(controller: LocalMediaPlayer, duration: Int, contentColor: Color) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        listOf(-10000 to R.string.ux30_chat_seek_back, 10000 to R.string.ux30_chat_seek_forward).forEach { (delta, resource) ->
            val label = stringResource(resource)
            TextButton(onClick = { controller.seek(chatSeekPosition(controller.positionMs, delta, duration)) },
                enabled = controller.ready && duration > 0 && if (delta < 0) controller.positionMs > 0 else controller.positionMs < duration,
                modifier = Modifier.heightIn(min = 48.dp).widthIn(min = 48.dp)
                    .testTag(if (delta < 0) "Chat.SeekBack" else "Chat.SeekForward")
                    .semantics { contentDescription = label },
                colors = ButtonDefaults.textButtonColors(contentColor = contentColor)) {
                Text(stringResource(if (delta < 0) R.string.ux30_chat_seek_back_short
                    else R.string.ux30_chat_seek_forward_short))
            }
        }
    }
}

@Composable
private fun PlaybackSpeedSelector(controller: LocalMediaPlayer, contentColor: Color) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(2.dp)) {
        chatMediaPlaybackSpeeds.forEach { speed ->
            val value = chatMediaPlaybackSpeedLabel(speed)
            val label = stringResource(R.string.ux60_chat_speed_accessible, value)
            TextButton(onClick = { controller.setSpeed(speed) }, enabled = controller.ready,
                modifier = Modifier.semantics {
                    contentDescription = label
                    selected = controller.playbackSpeed == speed
                }, colors = ButtonDefaults.textButtonColors(contentColor = contentColor)) {
                Text(value)
            }
        }
    }
    if (controller.speedFailed) Text(stringResource(R.string.ux60_chat_speed_failed),
        color = contentColor.copy(alpha = 0.72f), style = Zapara.typography.caption)
}

private fun centerCrop(view: TextureView, videoWidth: Int, videoHeight: Int) {
    if (view.width == 0 || view.height == 0 || videoWidth == 0 || videoHeight == 0) return
    val sourceRatio = videoWidth.toFloat() / videoHeight
    val targetRatio = view.width.toFloat() / view.height
    val scaleX = if (sourceRatio > targetRatio) sourceRatio / targetRatio else 1f
    val scaleY = if (sourceRatio < targetRatio) targetRatio / sourceRatio else 1f
    view.setTransform(Matrix().apply { setScale(scaleX, scaleY, view.width / 2f, view.height / 2f) })
}

private object ChatMediaPlayback {
    private val exclusive = ExclusivePlayback<LocalMediaPlayer> { it.pauseFromCoordinator() }

    fun start(player: LocalMediaPlayer) = exclusive.activate(player)

    fun release(player: LocalMediaPlayer) = exclusive.release(player)
}

internal class LocalMediaPlayer(private val file: File) {
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
    private val speedPolicy by lazy {
        PlaybackSpeedPolicy(
            isPlaying = { player?.isPlaying == true },
            activateExclusive = { ChatMediaPlayback.start(this) },
            applySpeed = { speed ->
                val media = player
                if (media == null) false else try {
                    media.playbackParams = PlaybackParams().setSpeed(speed)
                    true
                } catch (_: Exception) { false }
            },
            startPlayback = { player?.start() },
            releaseExclusive = { ChatMediaPlayback.release(this) },
            onPlayingChanged = { playing = it }
        )
    }
    val playbackSpeed get() = speedPolicy.speed
    val speedFailed get() = speedPolicy.failed
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
            media.setOnCompletionListener { playing = false; positionMs = 0; it.seekTo(0); ChatMediaPlayback.release(this) }
            media.setOnErrorListener { _, _, _ -> failed = true; playing = false; ChatMediaPlayback.release(this); true }
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
            if (media.isPlaying) {
                media.pause()
                ChatMediaPlayback.release(this)
            } else {
                speedPolicy.start()
            }
            playing = media.isPlaying
        } catch (_: Exception) { failed = true; playing = false; ChatMediaPlayback.release(this) }
    }

    fun setSpeed(speed: Float) {
        if (ready) speedPolicy.select(speed)
    }

    fun pause() {
        try {
            player?.takeIf { it.isPlaying }?.pause()
            playing = false
        } catch (_: Exception) { playing = false }
        ChatMediaPlayback.release(this)
    }

    internal fun pauseFromCoordinator() {
        try {
            player?.takeIf { it.isPlaying }?.pause()
            playing = false
        } catch (_: Exception) { playing = false }
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
        surface?.takeIf { it !== next }?.let { safeMediaPlayerLifecycleCall { it.release() } }
        surface = next
        val media = player
        if (media != null && !safeMediaPlayerLifecycleCall { media.setSurface(next) }) markPlayerFailure()
    }

    fun detach() {
        val media = player
        if (media != null && !safeMediaPlayerLifecycleCall { media.setSurface(null) }) markPlayerFailure()
        surface?.let { safeMediaPlayerLifecycleCall { it.release() } }
        surface = null
    }

    fun close() {
        ChatMediaPlayback.release(this)
        pauseFromCoordinator()
        playing = false
        ready = false
        val media = player
        player = null
        if (media != null) {
            safeMediaPlayerLifecycleCall { media.setSurface(null) }
            safeMediaPlayerLifecycleCall { media.release() }
        }
        surface?.let { safeMediaPlayerLifecycleCall { it.release() } }
        surface = null
    }

    private fun markPlayerFailure() {
        failed = true
        playing = false
        ChatMediaPlayback.release(this)
    }
}
