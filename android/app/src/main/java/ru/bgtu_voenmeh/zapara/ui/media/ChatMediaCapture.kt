package ru.bgtu_voenmeh.zapara.ui.media

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.media.MediaRecorder
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.CameraSelector
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.video.FallbackStrategy
import androidx.camera.video.FileOutputOptions
import androidx.camera.video.Quality
import androidx.camera.video.QualitySelector
import androidx.camera.video.Recorder
import androidx.camera.video.Recording
import androidx.camera.video.VideoCapture
import androidx.camera.video.VideoRecordEvent
import androidx.camera.view.PreviewView
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.R
import java.io.File

private enum class CaptureMode { Idle, Voice, CirclePreview, Circle, Finalizing }

/** The idle slot keeps each chat's own composer layout while sharing recording behavior. */
@Composable
fun ChatMediaCaptureHost(
    enabled: Boolean,
    onRecorded: (kind: String, file: File, durationMs: Int) -> Unit,
    onError: (String) -> Unit,
    modifier: Modifier = Modifier,
    idleContent: @Composable (startVoice: () -> Unit, startCircle: () -> Unit) -> Unit
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val recorded by rememberUpdatedState(onRecorded)
    val reportError by rememberUpdatedState(onError)
    val controller = remember(context) { ChatCaptureController(context) }
    controller.onRecorded = { kind, file, duration -> recorded(kind, file, duration) }
    controller.onError = { message -> reportError(message) }
    val previewView = remember(context) { PreviewView(context).apply {
        implementationMode = PreviewView.ImplementationMode.COMPATIBLE
        scaleType = PreviewView.ScaleType.FILL_CENTER
    } }
    val voicePermission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) controller.startVoice() else reportError(context.getString(R.string.chat_media_permission_voice))
    }
    val circlePermissions = rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { grants ->
        if (grants[Manifest.permission.CAMERA] == true && grants[Manifest.permission.RECORD_AUDIO] == true)
            controller.openCirclePreview()
        else reportError(context.getString(R.string.chat_media_permission_circle))
    }
    DisposableEffect(lifecycleOwner, controller) {
        val observer = LifecycleEventObserver { _, event -> if (event == Lifecycle.Event.ON_STOP) controller.cancel() }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose { lifecycleOwner.lifecycle.removeObserver(observer); controller.release() }
    }
    LaunchedEffect(controller.mode, lifecycleOwner) {
        if (controller.mode == CaptureMode.CirclePreview) controller.bindCamera(lifecycleOwner, previewView)
    }
    LaunchedEffect(controller.mode) {
        while (controller.mode == CaptureMode.Voice || controller.mode == CaptureMode.Circle) {
            controller.tick()
            delay(200)
        }
    }
    val startVoice = {
        if (enabled && controller.mode == CaptureMode.Idle) {
            if (ContextCompat.checkSelfPermission(context, Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED) controller.startVoice()
            else voicePermission.launch(Manifest.permission.RECORD_AUDIO)
        }
    }
    val startCircle = {
        if (enabled && controller.mode == CaptureMode.Idle) {
            if (ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED &&
                ContextCompat.checkSelfPermission(context, Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED) controller.openCirclePreview()
            else circlePermissions.launch(arrayOf(Manifest.permission.CAMERA, Manifest.permission.RECORD_AUDIO))
        }
    }
    Column(modifier, verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        when (controller.mode) {
            CaptureMode.Idle -> idleContent(startVoice, startCircle)
            CaptureMode.Voice -> Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                Text(stringResource(R.string.chat_media_recording, chatClock(controller.elapsedMs)), color = Zapara.colors.bad, modifier = Modifier.weight(1f))
                ZButton(stringResource(R.string.chat_media_cancel), { controller.cancel() }, ghost = true)
                ZButton(stringResource(R.string.chat_media_send), { controller.finishVoice(true) })
            }
            CaptureMode.CirclePreview, CaptureMode.Circle -> {
                AndroidView(factory = { previewView }, modifier = Modifier.align(Alignment.CenterHorizontally).size(224.dp).clip(CircleShape))
                Text(if (controller.mode == CaptureMode.Circle) stringResource(R.string.chat_media_recording, chatClock(controller.elapsedMs)) else if (controller.cameraReady) stringResource(R.string.chat_media_camera_ready) else stringResource(R.string.chat_media_camera_connecting),
                    color = if (controller.mode == CaptureMode.Circle) Zapara.colors.bad else Zapara.colors.text2,
                    modifier = Modifier.align(Alignment.CenterHorizontally))
                Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s), modifier = Modifier.align(Alignment.CenterHorizontally)) {
                    ZButton(stringResource(R.string.chat_media_cancel), { controller.cancel() }, ghost = true)
                    if (controller.mode == CaptureMode.Circle) ZButton(stringResource(R.string.chat_media_send), { controller.finishCircle(true) })
                    else ZButton(stringResource(R.string.chat_media_start), { controller.startCircle() }, enabled = controller.cameraReady)
                }
            }
            CaptureMode.Finalizing -> Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
                Text(stringResource(R.string.chat_media_finalizing), color = Zapara.colors.text2)
            }
        }
    }
}

internal fun chatClock(durationMs: Int): String {
    val seconds = (durationMs.coerceAtLeast(0) + 500) / 1000
    return "${seconds / 60}:${(seconds % 60).toString().padStart(2, '0')}"
}

private class ChatCaptureController(private val context: Context) {
    var mode by mutableStateOf(CaptureMode.Idle)
        private set
    var elapsedMs by mutableIntStateOf(0)
        private set
    var cameraReady by mutableStateOf(false)
        private set
    var onRecorded: (String, File, Int) -> Unit = { _, file, _ -> file.delete() }
    var onError: (String) -> Unit = {}

    private var startedAt = 0L
    private var voice: MediaRecorder? = null
    private var voiceFile: File? = null
    private var provider: ProcessCameraProvider? = null
    private var preview: Preview? = null
    private var capture: VideoCapture<Recorder>? = null
    private var recording: Recording? = null
    private var circleFile: File? = null
    private var circleSend = false
    private var released = false

    private fun newFile(extension: String): File {
        val directory = File(context.cacheDir, "chat-capture")
        if (!directory.isDirectory && !directory.mkdirs()) error(context.getString(R.string.chat_media_file_space))
        return File.createTempFile("recording-", extension, directory)
    }

    fun startVoice() {
        if (released || mode != CaptureMode.Idle) return
        val file = try { newFile(".m4a") } catch (_: Exception) { onError(context.getString(R.string.chat_media_prepare_failed)); return }
        val recorder = MediaRecorder()
        try {
            recorder.setAudioSource(MediaRecorder.AudioSource.MIC)
            recorder.setOutputFormat(MediaRecorder.OutputFormat.MPEG_4)
            recorder.setAudioEncoder(MediaRecorder.AudioEncoder.AAC)
            recorder.setAudioChannels(1)
            recorder.setAudioEncodingBitRate(24_000)
            recorder.setOutputFile(file.absolutePath)
            recorder.setMaxDuration(180_000)
            recorder.setMaxFileSize(2L * 1024 * 1024)
            recorder.setOnInfoListener { _, what, _ ->
                if (what == MediaRecorder.MEDIA_RECORDER_INFO_MAX_DURATION_REACHED || what == MediaRecorder.MEDIA_RECORDER_INFO_MAX_FILESIZE_REACHED)
                    finishVoice(true)
            }
            recorder.prepare()
            recorder.start()
            voice = recorder
            voiceFile = file
            startedAt = SystemClock.elapsedRealtime()
            elapsedMs = 0
            mode = CaptureMode.Voice
        } catch (_: Exception) {
            recorder.release()
            file.delete()
            onError(context.getString(R.string.chat_media_voice_start_failed))
        }
    }

    fun finishVoice(send: Boolean) {
        val recorder = voice ?: return
        val file = voiceFile
        voice = null
        voiceFile = null
        val duration = (SystemClock.elapsedRealtime() - startedAt).toInt().coerceIn(1, 180_000)
        val stopped = try { recorder.stop(); true } catch (_: Exception) { false }
        recorder.release()
        mode = CaptureMode.Idle
        elapsedMs = 0
        if (send && stopped && file != null && file.length() in 200..(2L * 1024 * 1024) && !released) onRecorded("voice", file, duration)
        else { file?.delete(); if (send && !released) onError(context.getString(R.string.chat_media_voice_invalid)) }
    }

    fun openCirclePreview() {
        if (released || mode != CaptureMode.Idle) return
        cameraReady = false
        elapsedMs = 0
        mode = CaptureMode.CirclePreview
    }

    suspend fun bindCamera(owner: androidx.lifecycle.LifecycleOwner, view: PreviewView) {
        try {
            val cameraProvider = withContext(Dispatchers.IO) { ProcessCameraProvider.getInstance(context).get() }
            if (released || mode != CaptureMode.CirclePreview) return
            val camera = if (cameraProvider.hasCamera(CameraSelector.DEFAULT_FRONT_CAMERA)) CameraSelector.DEFAULT_FRONT_CAMERA
                else CameraSelector.DEFAULT_BACK_CAMERA
            val nextPreview = Preview.Builder().build().apply { setSurfaceProvider(view.surfaceProvider) }
            val recorder = Recorder.Builder()
                .setQualitySelector(QualitySelector.from(Quality.SD, FallbackStrategy.lowerQualityOrHigherThan(Quality.SD)))
                .setTargetVideoEncodingBitRate(650_000)
                .build()
            val nextCapture = VideoCapture.withOutput(recorder)
            cameraProvider.bindToLifecycle(owner, camera, nextPreview, nextCapture)
            provider = cameraProvider
            preview = nextPreview
            capture = nextCapture
            cameraReady = true
        } catch (_: Exception) {
            unbindCamera()
            mode = CaptureMode.Idle
            if (!released) onError(context.getString(R.string.chat_media_camera_open_failed))
        }
    }

    fun startCircle() {
        val video = capture ?: return
        if (released || mode != CaptureMode.CirclePreview || !cameraReady) return
        val file = try { newFile(".mp4") } catch (_: Exception) { onError(context.getString(R.string.chat_media_prepare_failed)); return }
        try {
            circleFile = file
            circleSend = false
            val options = FileOutputOptions.Builder(file).build()
            recording = video.output.prepareRecording(context, options).withAudioEnabled()
                .start(ContextCompat.getMainExecutor(context)) { event ->
                    when (event) {
                        is VideoRecordEvent.Status -> {
                            if (event.recordingStats.numBytesRecorded > 7L * 1024 * 1024) finishCircle(true)
                        }
                        is VideoRecordEvent.Finalize -> Handler(Looper.getMainLooper()).post { finishCircleFile(event.hasError()) }
                    }
                }
            startedAt = SystemClock.elapsedRealtime()
            elapsedMs = 0
            mode = CaptureMode.Circle
        } catch (_: Exception) {
            recording = null
            circleFile = null
            file.delete()
            onError(context.getString(R.string.chat_media_circle_start_failed))
        }
    }

    fun finishCircle(send: Boolean) {
        if (mode != CaptureMode.Circle) return
        circleSend = send
        elapsedMs = (SystemClock.elapsedRealtime() - startedAt).toInt().coerceIn(1, 60_000)
        mode = CaptureMode.Finalizing
        recording?.stop()
    }

    private fun finishCircleFile(failed: Boolean) {
        val file = circleFile
        val send = circleSend
        val duration = elapsedMs.coerceIn(1, 60_000)
        recording = null
        circleFile = null
        circleSend = false
        unbindCamera()
        mode = CaptureMode.Idle
        elapsedMs = 0
        if (send && !failed && file != null && file.length() in 1000..(8L * 1024 * 1024) && !released) onRecorded("circle", file, duration)
        else { file?.delete(); if (send && !released) onError(context.getString(R.string.chat_media_circle_invalid)) }
    }

    fun tick() {
        if (mode != CaptureMode.Voice && mode != CaptureMode.Circle) return
        elapsedMs = (SystemClock.elapsedRealtime() - startedAt).toInt().coerceAtLeast(0)
        if (mode == CaptureMode.Voice && elapsedMs >= 180_000) finishVoice(true)
        if (mode == CaptureMode.Circle && elapsedMs >= 60_000) finishCircle(true)
    }

    fun cancel() {
        when (mode) {
            CaptureMode.Voice -> finishVoice(false)
            CaptureMode.Circle -> finishCircle(false)
            CaptureMode.CirclePreview -> { unbindCamera(); mode = CaptureMode.Idle }
            CaptureMode.Finalizing -> circleSend = false
            CaptureMode.Idle -> Unit
        }
    }

    fun release() {
        released = true
        cancel()
        unbindCamera()
        voiceFile?.delete()
        if (recording == null) circleFile?.delete()
    }

    private fun unbindCamera() {
        val oldPreview = preview
        val oldCapture = capture
        if (oldPreview != null && oldCapture != null) provider?.unbind(oldPreview, oldCapture)
        preview = null
        capture = null
        provider = null
        cameraReady = false
    }
}
