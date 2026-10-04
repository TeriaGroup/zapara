package ru.bgtu_voenmeh.zapara.ui.media

import android.Manifest
import android.content.Context
import android.content.pm.ApplicationInfo
import android.content.pm.PackageManager
import android.media.MediaRecorder
import android.os.Handler
import android.os.Looper
import android.util.Log
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
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.ime
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
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.Observer
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.R
import java.io.File

private enum class CaptureMode { Idle, Voice, CirclePreview, Circle, Finalizing, Review }

/** The idle slot keeps each chat's own composer layout while sharing recording behavior. */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun ChatMediaCaptureHost(
    enabled: Boolean,
    onRecorded: (kind: String, file: File, durationMs: Int) -> Unit,
    onError: (String) -> Unit,
    modifier: Modifier = Modifier,
    idleContent: @Composable (startVoice: () -> Unit, startCircle: () -> Unit) -> Unit
) {
    val context = LocalContext.current
    val focusManager = LocalFocusManager.current
    val keyboardController = LocalSoftwareKeyboardController.current
    val density = LocalDensity.current
    val screenHeight = LocalConfiguration.current.screenHeightDp.dp
    val keyboardHeight = with(density) { WindowInsets.ime.getBottom(this).toDp() }
    val previewSize = minOf(224.dp, (screenHeight - keyboardHeight).coerceAtLeast(400.dp) * 0.28f)
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
            focusManager.clearFocus(force = true)
            keyboardController?.hide()
            if (ContextCompat.checkSelfPermission(context, Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED) controller.startVoice()
            else voicePermission.launch(Manifest.permission.RECORD_AUDIO)
        }
    }
    val startCircle = {
        if (enabled && controller.mode == CaptureMode.Idle) {
            focusManager.clearFocus(force = true)
            keyboardController?.hide()
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
                ZButton(stringResource(R.string.ux60_chat_record_review), { controller.finishVoice(true) })
            }
            CaptureMode.CirclePreview, CaptureMode.Circle -> {
                AndroidView(factory = { previewView }, modifier = Modifier.align(Alignment.CenterHorizontally).size(previewSize).clip(CircleShape))
                Text(if (controller.mode == CaptureMode.Circle) stringResource(R.string.chat_media_recording, chatClock(controller.elapsedMs)) else if (controller.cameraReady) stringResource(R.string.chat_media_camera_ready) else stringResource(R.string.chat_media_camera_connecting),
                    color = if (controller.mode == CaptureMode.Circle) Zapara.colors.bad else Zapara.colors.text2,
                    modifier = Modifier.align(Alignment.CenterHorizontally))
                Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s), modifier = Modifier.align(Alignment.CenterHorizontally)) {
                    ZButton(stringResource(R.string.chat_media_cancel), { controller.cancel() }, ghost = true)
                    if (controller.mode == CaptureMode.Circle) ZButton(stringResource(R.string.ux60_chat_record_review), { controller.finishCircle(true) })
                    else ZButton(stringResource(R.string.chat_media_start), { controller.startCircle() }, enabled = controller.cameraReady)
                }
            }
            CaptureMode.Finalizing -> Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
                Text(stringResource(R.string.chat_media_finalizing), color = Zapara.colors.text2)
            }
            CaptureMode.Review -> Column(verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                Text(stringResource(R.string.ux60_chat_record_review_title), color = Zapara.colors.text2,
                    style = Zapara.typography.caption)
                controller.reviewFile?.let { file ->
                    ChatMediaBubble(controller.reviewKind.orEmpty(), file, controller.reviewDurationMs,
                        loading = false, error = false, onLoad = {}, modifier = Modifier.align(Alignment.CenterHorizontally))
                }
                FlowRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                    verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    ZButton(stringResource(R.string.ux60_chat_record_discard), { controller.discardReview() }, ghost = true)
                    ZButton(stringResource(R.string.ux60_chat_record_again), { controller.recordAgain() }, enabled = enabled, ghost = true)
                }
                ZButton(stringResource(R.string.ux60_chat_record_send), { controller.sendReview() }, enabled = enabled,
                    modifier = Modifier.fillMaxWidth())
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
    private val reviewState = ChatRecordingReview()
    val reviewFile get() = reviewState.current?.file
    val reviewKind get() = reviewState.current?.kind
    val reviewDurationMs get() = reviewState.current?.durationMs ?: 0

    private var startedAt = 0L
    private var voice: MediaRecorder? = null
    private var voiceFile: File? = null
    private var provider: ProcessCameraProvider? = null
    private var preview: Preview? = null
    private var observedPreviewView: PreviewView? = null
    private var previewStreamObserver: Observer<PreviewView.StreamState>? = null
    private var capture: VideoCapture<Recorder>? = null
    private var recording: Recording? = null
    private var circleFile: File? = null
    private var circleAudio: MediaRecorder? = null
    private var circleAudioFile: File? = null
    private var circleAudioRate = 0
    private var circleVideoStartNanos = 0L
    private var circleAudioStartCallNanos = 0L
    private var circleAudioStartNanos = 0L
    private var circleAudioFailed = false
    private var circleGeneration = 0
    private val finalizer = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private var finalizeJob: Job? = null
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
        for (sampleRate in listOf(48_000, 44_100, null)) {
            val recorder = MediaRecorder()
            try {
                recorder.setAudioSource(MediaRecorder.AudioSource.MIC)
                recorder.setOutputFormat(MediaRecorder.OutputFormat.MPEG_4)
                recorder.setAudioEncoder(MediaRecorder.AudioEncoder.AAC)
                recorder.setAudioChannels(1)
                if (sampleRate != null) recorder.setAudioSamplingRate(sampleRate)
                recorder.setAudioEncodingBitRate(96_000)
                recorder.setOutputFile(file.absolutePath)
                recorder.setMaxDuration(180_000)
                recorder.setMaxFileSize(4L * 1024 * 1024)
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
                return
            } catch (_: Exception) {
                runCatching { recorder.release() }
            }
        }
        file.delete()
        onError(context.getString(R.string.chat_media_voice_start_failed))
    }

    fun finishVoice(send: Boolean) {
        val recorder = voice ?: return
        val file = voiceFile
        voice = null
        voiceFile = null
        val duration = (SystemClock.elapsedRealtime() - startedAt).toInt().coerceIn(1, 180_000)
        val stopped = try { recorder.stop(); true } catch (_: Exception) { false }
        recorder.release()
        elapsedMs = 0
        if (send && stopped && file != null && file.length() in 200..(4L * 1024 * 1024) && !released) {
            reviewState.preview(ReviewedChatRecording("voice", file, duration))
            mode = CaptureMode.Review
        } else {
            file?.delete()
            mode = CaptureMode.Idle
            if (send && !released) onError(context.getString(R.string.chat_media_voice_invalid))
        }
    }

    fun sendReview() {
        if (mode != CaptureMode.Review) return
        val recording = reviewState.takeForSend() ?: return
        mode = CaptureMode.Idle
        try { onRecorded(recording.kind, recording.file, recording.durationMs) }
        catch (error: Exception) {
            recording.file.delete()
            onError(context.getString(R.string.ux60_chat_record_send_failed))
        }
    }

    fun discardReview() {
        reviewState.discard()
        mode = CaptureMode.Idle
    }

    fun recordAgain() {
        if (mode != CaptureMode.Review) return
        val kind = reviewKind
        discardReview()
        if (kind == "circle") openCirclePreview() else startVoice()
    }

    fun openCirclePreview() {
        if (released || mode != CaptureMode.Idle) return
        cameraReady = false
        elapsedMs = 0
        mode = CaptureMode.CirclePreview
    }

    suspend fun bindCamera(owner: androidx.lifecycle.LifecycleOwner, view: PreviewView) = withContext(Dispatchers.Main.immediate) {
        try {
            val cameraProvider = withContext(Dispatchers.IO) { ProcessCameraProvider.getInstance(context).get() }
            if (released || mode != CaptureMode.CirclePreview) return@withContext
            val camera = if (cameraProvider.hasCamera(CameraSelector.DEFAULT_FRONT_CAMERA)) CameraSelector.DEFAULT_FRONT_CAMERA
                else CameraSelector.DEFAULT_BACK_CAMERA
            val nextPreview = Preview.Builder().build().apply { setSurfaceProvider(view.surfaceProvider) }
            var nextCapture: VideoCapture<Recorder>? = null
            for (quality in listOf(Quality.HD, Quality.SD)) {
                val recorder = Recorder.Builder()
                    .setQualitySelector(QualitySelector.from(quality, FallbackStrategy.lowerQualityOrHigherThan(quality)))
                    .setTargetVideoEncodingBitRate(2_000_000)
                    .build()
                val candidate = VideoCapture.withOutput(recorder)
                try {
                    cameraProvider.bindToLifecycle(owner, camera, nextPreview, candidate)
                    nextCapture = candidate
                    break
                } catch (cancelled: CancellationException) {
                    runCatching { cameraProvider.unbind(nextPreview, candidate) }
                    throw cancelled
                } catch (error: Exception) {
                    logCameraFailure("Failed to bind ${if (quality == Quality.HD) "HD" else "SD"} video", error)
                    runCatching { cameraProvider.unbind(nextPreview, candidate) }
                    if (quality == Quality.SD) throw error
                }
            }
            val boundCapture = checkNotNull(nextCapture)
            provider = cameraProvider
            preview = nextPreview
            capture = boundCapture
            // A successful bind does not mean the first camera frame is visible.
            // COMPATIBLE PreviewView reports STREAMING after its texture receives it.
            val streamObserver = Observer<PreviewView.StreamState> { state ->
                cameraReady = !released && capture === boundCapture && state == PreviewView.StreamState.STREAMING
            }
            observedPreviewView = view
            previewStreamObserver = streamObserver
            view.previewStreamState.observe(owner, streamObserver)
        } catch (cancelled: CancellationException) {
            unbindCamera()
            throw cancelled
        } catch (error: Exception) {
            logCameraFailure("Unable to open camera for circle recording", error)
            unbindCamera()
            mode = CaptureMode.Idle
            if (!released) onError(context.getString(R.string.chat_media_camera_open_failed))
        }
    }

    private fun logCameraFailure(message: String, error: Exception) {
        if ((context.applicationInfo.flags and ApplicationInfo.FLAG_DEBUGGABLE) != 0)
            Log.w("ChatMediaCapture", message, error)
    }

    private fun prepareCircleAudio(sampleRates: List<Int> = listOf(48_000, 44_100)): Boolean {
        for (sampleRate in sampleRates) {
            val file = try { newFile(".m4a") } catch (_: Exception) { return false }
            val recorder = MediaRecorder()
            try {
                recorder.setAudioSource(MediaRecorder.AudioSource.MIC)
                recorder.setOutputFormat(MediaRecorder.OutputFormat.MPEG_4)
                recorder.setAudioEncoder(MediaRecorder.AudioEncoder.AAC)
                recorder.setAudioChannels(1)
                recorder.setAudioSamplingRate(sampleRate)
                recorder.setAudioEncodingBitRate(96_000)
                recorder.setOutputFile(file.absolutePath)
                recorder.setMaxDuration(61_000)
                recorder.setMaxFileSize(4L * 1024 * 1024)
                recorder.prepare()
                circleAudio = recorder
                circleAudioFile = file
                circleAudioRate = sampleRate
                return true
            } catch (_: Exception) {
                runCatching { recorder.release() }
                file.delete()
            }
        }
        return false
    }

    private fun startCircleAudio(): Boolean {
        for (attempt in 0..1) {
            val recorder = circleAudio ?: return false
            try {
                circleAudioStartCallNanos = SystemClock.elapsedRealtimeNanos()
                recorder.start()
                circleAudioStartNanos = SystemClock.elapsedRealtimeNanos()
                return true
            } catch (_: Exception) {
                val retry = attempt == 0 && circleAudioRate == 48_000
                discardCircleAudio()
                if (!retry || !prepareCircleAudio(listOf(44_100))) return false
            }
        }
        return false
    }

    private fun stopCircleAudio(reportMissing: Boolean) {
        val recorder = circleAudio ?: return
        circleAudio = null
        val started = circleAudioStartNanos > 0
        val stopped = if (started) try { recorder.stop(); true } catch (_: Exception) { false } else false
        runCatching { recorder.release() }
        if (!stopped) {
            if (reportMissing) circleAudioFailed = true
            circleAudioFile?.delete()
            circleAudioFile = null
        }
    }

    private fun discardCircleAudio() {
        runCatching { circleAudio?.release() }
        circleAudio = null
        circleAudioFile?.delete()
        circleAudioFile = null
        circleAudioRate = 0
        circleAudioStartCallNanos = 0L
        circleAudioStartNanos = 0L
    }

    fun startCircle() {
        val video = capture ?: return
        if (released || mode != CaptureMode.CirclePreview || !cameraReady) return
        val file = try { newFile(".mp4") } catch (_: Exception) { onError(context.getString(R.string.chat_media_prepare_failed)); return }
        if (!prepareCircleAudio()) {
            file.delete()
            onError(context.getString(R.string.chat_media_circle_start_failed))
            return
        }
        try {
            circleFile = file
            circleSend = false
            circleAudioFailed = false
            circleVideoStartNanos = 0L
            circleAudioStartCallNanos = 0L
            circleAudioStartNanos = 0L
            val generation = ++circleGeneration
            val options = FileOutputOptions.Builder(file).build()
            startedAt = SystemClock.elapsedRealtime()
            elapsedMs = 0
            mode = CaptureMode.Circle
            recording = video.output.prepareRecording(context, options)
                .start(ContextCompat.getMainExecutor(context)) { event ->
                    if (generation == circleGeneration) {
                        when (event) {
                            is VideoRecordEvent.Start -> if (mode == CaptureMode.Circle && !released) {
                                circleVideoStartNanos = SystemClock.elapsedRealtimeNanos()
                                if (!startCircleAudio()) {
                                    circleAudioFailed = true
                                    finishCircle(false)
                                }
                            }
                            is VideoRecordEvent.Status -> {
                                if (event.recordingStats.numBytesRecorded > 22L * 1024 * 1024) finishCircle(true)
                            }
                            is VideoRecordEvent.Finalize -> Handler(Looper.getMainLooper()).post {
                                if (generation == circleGeneration) finishCircleFile(event.hasError())
                            }
                        }
                    }
                }
        } catch (_: Exception) {
            recording = null
            circleFile = null
            discardCircleAudio()
            file.delete()
            mode = CaptureMode.CirclePreview
            onError(context.getString(R.string.chat_media_circle_start_failed))
        }
    }

    fun finishCircle(send: Boolean) {
        if (mode != CaptureMode.Circle) return
        circleSend = send
        elapsedMs = (SystemClock.elapsedRealtime() - startedAt).toInt().coerceIn(1, 60_000)
        mode = CaptureMode.Finalizing
        stopCircleAudio(reportMissing = send)
        recording?.stop()
    }

    private fun finishCircleFile(failed: Boolean) {
        val unexpected = mode == CaptureMode.Circle
        if (unexpected) {
            elapsedMs = (SystemClock.elapsedRealtime() - startedAt).toInt().coerceIn(1, 60_000)
            stopCircleAudio(reportMissing = true)
        }
        val file = circleFile
        val audioFile = circleAudioFile
        val send = circleSend
        val audioFailure = circleAudioFailed
        val duration = elapsedMs.coerceIn(1, 60_000)
        val audioEpoch = circleAudioStartCallNanos + (circleAudioStartNanos - circleAudioStartCallNanos) / 2
        val audioDelayUs = ((audioEpoch - circleVideoStartNanos) / 1000).coerceAtLeast(0)
        val valid = !failed && !circleAudioFailed && circleVideoStartNanos > 0 &&
            circleAudioStartNanos > 0 && audioDelayUs <= 2_000_000 &&
            file != null && file.length() in 1000..(24L * 1024 * 1024) &&
            audioFile != null && audioFile.length() in 200..(4L * 1024 * 1024)
        recording = null
        circleFile = null
        circleAudioFile = null
        circleAudio = null
        circleAudioRate = 0
        circleVideoStartNanos = 0L
        circleAudioStartCallNanos = 0L
        circleAudioStartNanos = 0L
        circleAudioFailed = false
        unbindCamera()
        elapsedMs = 0
        if (!send || !valid || released) {
            file?.delete()
            audioFile?.delete()
            circleSend = false
            mode = CaptureMode.Idle
            if ((send || unexpected || audioFailure) && !released) onError(context.getString(R.string.chat_media_circle_invalid))
            return
        }
        val output = try { newFile(".mp4") } catch (_: Exception) {
            file.delete()
            audioFile.delete()
            circleSend = false
            mode = CaptureMode.Idle
            onError(context.getString(R.string.chat_media_circle_invalid))
            return
        }
        finalizeJob = finalizer.launch {
            var delivered = false
            try {
                val job = currentCoroutineContext()[Job] ?: error("Circle finalizer has no job")
                withContext(Dispatchers.IO) {
                    ChatCircleMux.merge(file, audioFile, output, audioDelayUs) { job.ensureActive() }
                }
                if (circleSend && !released) {
                    reviewState.preview(ReviewedChatRecording("circle", output, duration))
                    mode = CaptureMode.Review
                    delivered = true
                }
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (_: Exception) {
                if (!released) onError(context.getString(R.string.chat_media_circle_invalid))
            } finally {
                file.delete()
                audioFile.delete()
                if (!delivered) output.delete()
                circleSend = false
                if (mode == CaptureMode.Finalizing) mode = CaptureMode.Idle
                finalizeJob = null
            }
        }
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
            CaptureMode.Review -> discardReview()
            CaptureMode.Finalizing -> circleSend = false
            CaptureMode.Idle -> Unit
        }
    }

    fun release() {
        released = true
        cancel()
        finalizer.cancel()
        unbindCamera()
        voiceFile?.delete()
        if (recording == null) {
            circleFile?.delete()
            discardCircleAudio()
        } else {
            val pendingVideo = circleFile
            val pendingAudio = circleAudioFile
            Handler(Looper.getMainLooper()).postDelayed({
                if (released && circleFile == pendingVideo) {
                    pendingVideo?.delete()
                    pendingAudio?.delete()
                    circleFile = null
                    circleAudioFile = null
                }
            }, 10_000)
        }
    }

    private fun unbindCamera() {
        previewStreamObserver?.let { observedPreviewView?.previewStreamState?.removeObserver(it) }
        previewStreamObserver = null
        observedPreviewView = null
        val oldPreview = preview
        val oldCapture = capture
        if (oldPreview != null && oldCapture != null) provider?.unbind(oldPreview, oldCapture)
        preview = null
        capture = null
        provider = null
        cameraReady = false
    }
}
