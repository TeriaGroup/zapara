package ru.bgtu_voenmeh.zapara

import android.Manifest
import android.content.pm.PackageManager
import android.media.MediaExtractor
import android.media.MediaFormat
import android.media.MediaMetadataRetriever
import android.media.MediaPlayer
import android.os.SystemClock
import androidx.activity.ComponentActivity
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onAllNodesWithTag
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.unit.dp
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import ru.bgtu_voenmeh.zapara.ui.media.ChatMediaCaptureHost
import ru.bgtu_voenmeh.zapara.ui.theme.MotionSettings
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme
import java.io.File
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference
import kotlin.math.abs

/** Root harness pregrants and later restores camera/microphone permissions. Extractor checks validate containers/tracks; decoding is checked separately. */
@RunWith(AndroidJUnit4::class)
class ChatCaptureQualityTest {
    @get:Rule val rule = createAndroidComposeRule<ComponentActivity>()

    private val instrumentation get() = InstrumentationRegistry.getInstrumentation()
    private val context get() = instrumentation.targetContext
    private val callback = AtomicReference<Recorded?>(null)
    private val errors = CopyOnWriteArrayList<String>()
    private lateinit var evidenceDirectory: File
    private lateinit var captureCacheDirectory: File

    @Before fun requireCapturePermissionsGrantedByHarness() {
        assertEquals("Root harness must grant RECORD_AUDIO before the suite", PackageManager.PERMISSION_GRANTED,
            context.checkSelfPermission(Manifest.permission.RECORD_AUDIO))
        assertEquals("Root harness must grant CAMERA before the suite", PackageManager.PERMISSION_GRANTED,
            context.checkSelfPermission(Manifest.permission.CAMERA))
        val root = requireNotNull(rule.activity.getExternalFilesDir(null))
        evidenceDirectory = File(root, "chat-capture-evidence/run-${System.currentTimeMillis()}").apply {
            check(isDirectory || mkdirs()) { "Could not create capture evidence directory: $absolutePath" }
        }
        captureCacheDirectory = File(context.cacheDir, "chat-capture")
    }

    @Test fun voice_records_aac_container_with_audio_samples_and_actual_encoder_metadata() {
        showHost()
        rule.onNodeWithTag("Capture.StartVoice").performClick()
        waitForRecording()
        capture("voice-recording")
        SystemClock.sleep(RECORDING_MILLIS)
        rule.onNodeWithText(rule.activity.getString(R.string.chat_media_send)).performClick()

        val recorded = awaitCallback("voice")
        val media = inspect(recorded.file)
        val decoding = decodeAudioPlayback(recorded.file)
        media.json.put("decode", decoding)
        writeEvidence("voice", recorded, media, JSONObject()
            .put("codec", "AAC")
            .put("sample_rate_hz", 48_000)
            .put("fallback_sample_rate_hz", JSONArray().put(44_100).put("encoder default"))
            .put("channels", 1)
            .put("bitrate_bps", 96_000)
            .put("max_bytes", VOICE_MAX_BYTES))
        assertTrue("Voice recording must fit the 4 MiB cap: ${recorded.file.length()} bytes", recorded.file.length() in 200..VOICE_MAX_BYTES)
        assertTrue("Voice container duration should cover the capture: ${media.durationMs} ms", media.durationMs >= MIN_MEDIA_DURATION_MS)
        val audio = media.tracks.singleOrNull { it.mime.startsWith("audio/") }
        assertNotNull("Voice container must contain one audio track: ${media.json}", audio)
        assertTrue("Voice codec must be AAC: ${audio?.mime}", audio?.mime == "audio/mp4a-latm")
        assertTrue("Extractor must demux audio samples from the voice container", (audio?.sampleCount ?: 0) > 0)
        assertEquals("Voice recording should be mono", 1, audio?.channels)
        assertTrue("Audio sample rate must be readable: ${audio?.sampleRate}", (audio?.sampleRate ?: 0) > 0)
        assertTrue("Voice callback duration should cover the capture: ${recorded.durationMs}", recorded.durationMs >= MIN_CAPTURE_DURATION_MS)
        assertTrue("Voice media must prepare, play, and complete: ${decoding.optString("error")}", decoding.optBoolean("completed"))
    }

    @Test fun circle_preview_records_audio_video_container_and_actual_encoder_metadata() {
        showHost()
        rule.onNodeWithTag("Capture.StartCircle").performClick()
        awaitCameraReady()
        capture("circle-preview")
        rule.onNodeWithText(rule.activity.getString(R.string.chat_media_start)).performClick()
        waitForRecording()
        capture("circle-recording")
        SystemClock.sleep(RECORDING_MILLIS)
        rule.onNodeWithText(rule.activity.getString(R.string.chat_media_send)).performClick()

        val recorded = awaitCallback("circle", 30_000)
        val media = inspect(recorded.file)
        val audio = media.tracks.singleOrNull { it.mime.startsWith("audio/") }
        val video = media.tracks.singleOrNull { it.mime.startsWith("video/") }
        val audioDurationMs = audio?.durationMs()
        val videoDurationMs = video?.durationMs()
        val durationDeltaMs = if (audioDurationMs != null && videoDurationMs != null) abs(audioDurationMs - videoDurationMs) else null
        val audioDecoding = decodeAudioPlayback(recorded.file)
        val videoDecoding = decodeCircleFrame(recorded.file)
        media.json.put("audio_duration_ms", audioDurationMs ?: JSONObject.NULL)
            .put("video_duration_ms", videoDurationMs ?: JSONObject.NULL)
            .put("track_duration_delta_ms", durationDeltaMs ?: JSONObject.NULL)
            .put("decode", JSONObject().put("audio", audioDecoding).put("video", videoDecoding))
        writeEvidence("circle", recorded, media, JSONObject()
            .put("quality", "CameraX Quality.HD with fallback")
            .put("target_video_bitrate_bps", 2_000_000)
            .put("max_bytes", CIRCLE_MAX_BYTES))
        assertTrue("Circle recording must fit the 24 MiB cap: ${recorded.file.length()} bytes", recorded.file.length() in 1000..CIRCLE_MAX_BYTES)
        assertTrue("Circle container duration should cover the capture: ${media.durationMs} ms", media.durationMs >= MIN_MEDIA_DURATION_MS)
        assertNotNull("Circle container must contain an audio track: ${media.json}", audio)
        assertNotNull("Circle container must contain a video track: ${media.json}", video)
        assertEquals("Circle audio codec must be AAC", "audio/mp4a-latm", audio?.mime)
        assertTrue("Extractor must demux audio samples from the circle container", (audio?.sampleCount ?: 0) > 0)
        assertTrue("Extractor must demux video samples from the circle container", (video?.sampleCount ?: 0) > 0)
        assertTrue("Circle audio must be mono: ${audio?.channels}", audio?.channels == 1)
        assertTrue("Circle audio should use 48 kHz or 44.1 kHz: ${audio?.sampleRate}", audio?.sampleRate == 48_000 || audio?.sampleRate == 44_100)
        val reportedAudioBitrate = audio?.bitrate
        if (reportedAudioBitrate != null) {
            assertTrue("Reported circle AAC bitrate should be near 96 kbps: $reportedAudioBitrate", reportedAudioBitrate in CIRCLE_AUDIO_BITRATE_RANGE)
        }
        assertTrue("Circle video dimensions must be readable: ${video?.width}x${video?.height}", (video?.width ?: 0) >= 240 && (video?.height ?: 0) >= 240)
        assertTrue("Circle callback duration should cover the capture: ${recorded.durationMs}", recorded.durationMs >= MIN_CAPTURE_DURATION_MS)
        assertTrue("Circle media must decode AAC audio: ${audioDecoding.optString("error")}", audioDecoding.optBoolean("completed"))
        assertTrue("Circle media must decode a video frame: ${videoDecoding.optString("error")}", videoDecoding.optBoolean("frame_decoded"))
        assertNotNull("Circle audio track duration must be readable: ${media.json}", audioDurationMs)
        assertNotNull("Circle video track duration must be readable: ${media.json}", videoDurationMs)
        assertTrue("Circle audio/video durations should align within 250 ms: ${media.json}", durationDeltaMs != null && durationDeltaMs <= CIRCLE_TRACK_DELTA_TOLERANCE_MS)
    }

    @Test fun canceling_voice_recording_does_not_call_recorded_callback() {
        showHost()
        rule.onNodeWithTag("Capture.StartVoice").performClick()
        waitForRecording()
        SystemClock.sleep(900)
        rule.onNodeWithText(rule.activity.getString(R.string.chat_media_cancel)).performClick()
        awaitIdleHost()
        assertFalse("Cancel must not deliver a media callback", callback.get() != null)
        assertTrue("Cancel must not report a capture error: $errors", errors.isEmpty())
    }

    @Test fun canceling_circle_recording_removes_its_temp_file_without_callback() {
        val preexistingFiles = captureCacheDirectory.listFiles().orEmpty().mapTo(HashSet()) { it.canonicalPath }
        showHost()
        rule.onNodeWithTag("Capture.StartCircle").performClick()
        awaitCameraReady()
        rule.onNodeWithText(rule.activity.getString(R.string.chat_media_start)).performClick()
        waitForRecording()
        SystemClock.sleep(900)
        rule.onNodeWithText(rule.activity.getString(R.string.chat_media_cancel)).performClick()
        awaitIdleHost("Capture.StartCircle")
        rule.waitUntil(15_000) { captureCacheDirectory.listFiles().orEmpty().none { it.canonicalPath !in preexistingFiles } }
        assertFalse("Cancel must not deliver a media callback", callback.get() != null)
        assertTrue("Cancel must not report a capture error: $errors", errors.isEmpty())
    }

    private fun showHost() {
        rule.setContent {
            ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
                var errorMessage by remember { mutableStateOf<String?>(null) }
                Column(Modifier.fillMaxSize().padding(16.dp)) {
                    ChatMediaCaptureHost(
                        enabled = true,
                        onRecorded = { kind, file, duration ->
                            val copy = File(evidenceDirectory, if (kind == "voice") "voice.m4a" else "circle.mp4")
                            try {
                                file.copyTo(copy, overwrite = true)
                                callback.set(Recorded(kind, copy, duration))
                            } finally {
                                file.delete()
                            }
                        },
                        onError = { errors.add(it); errorMessage = it },
                        modifier = Modifier.weight(1f)
                    ) { startVoice, startCircle ->
                        ZButton("Тест: голосовое", startVoice, Modifier.testTag("Capture.StartVoice"))
                        ZButton("Тест: кружок", startCircle, Modifier.testTag("Capture.StartCircle"))
                    }
                    errorMessage?.let { Text(it, Modifier.testTag("Capture.Error")) }
                }
            }
        }
        rule.waitForIdle()
    }

    private fun waitForRecording() {
        val marker = rule.activity.getString(R.string.chat_media_recording, "")
            .substringBeforeLast(' ').trim()
        rule.waitUntil(15_000) {
            rule.onAllNodesWithText(marker, substring = true).fetchSemanticsNodes().isNotEmpty() || errors.isNotEmpty()
        }
        assertTrue("Capture host reported an error: $errors", errors.isEmpty())
    }

    private fun awaitCameraReady() {
        val ready = rule.activity.getString(R.string.chat_media_camera_ready)
        rule.waitUntil(30_000) {
            rule.onAllNodesWithText(ready).fetchSemanticsNodes().isNotEmpty() || errors.isNotEmpty()
        }
        if (errors.isNotEmpty()) capture("circle-camera-error")
        assertTrue("Camera preview could not start: $errors", errors.isEmpty())
        assertTrue("Camera did not reach ready state", rule.onAllNodesWithText(ready).fetchSemanticsNodes().isNotEmpty())
    }

    private fun awaitCallback(kind: String, timeoutMs: Long = 15_000): Recorded {
        rule.waitUntil(timeoutMs) { callback.get() != null || errors.isNotEmpty() }
        assertTrue("Capture failed: $errors", errors.isEmpty())
        val result = requireNotNull(callback.get()) { "No media callback for $kind" }
        assertEquals("Unexpected callback kind", kind, result.kind)
        return result
    }

    private fun awaitIdleHost(idleTag: String = "Capture.StartVoice") {
        rule.waitUntil(5_000) {
            rule.onAllNodesWithTag(idleTag).fetchSemanticsNodes().isNotEmpty() &&
                rule.onAllNodesWithText(rule.activity.getString(R.string.chat_media_recording, ""), substring = true).fetchSemanticsNodes().isEmpty()
        }
    }

    private fun capture(name: String) {
        if (InstrumentationRegistry.getArguments().getString("chatEvidence") != "true") return
        rule.waitForIdle()
        Thread.sleep(200)
        instrumentation.uiAutomation.takeScreenshot()?.let { bitmap ->
            File(evidenceDirectory, "$name.png").outputStream().use { bitmap.compress(android.graphics.Bitmap.CompressFormat.PNG, 100, it) }
            bitmap.recycle()
        }
    }

    private fun inspect(file: File): MediaInspection {
        val extractor = MediaExtractor()
        extractor.setDataSource(file.absolutePath)
        try {
            val tracks = (0 until extractor.trackCount).map { index ->
                val format = extractor.getTrackFormat(index)
                val track = MediaTrack(
                    mime = format.getString(MediaFormat.KEY_MIME).orEmpty(),
                    channels = format.intOrNull(MediaFormat.KEY_CHANNEL_COUNT),
                    sampleRate = format.intOrNull(MediaFormat.KEY_SAMPLE_RATE),
                    bitrate = format.intOrNull(MediaFormat.KEY_BIT_RATE),
                    width = format.intOrNull(MediaFormat.KEY_WIDTH),
                    height = format.intOrNull(MediaFormat.KEY_HEIGHT),
                    durationUs = format.longOrNull(MediaFormat.KEY_DURATION)
                )
                extractor.selectTrack(index)
                track
            }
            val sampleCounts = IntArray(tracks.size)
            val firstSampleTimesUs = LongArray(tracks.size) { Long.MAX_VALUE }
            val lastSampleTimesUs = LongArray(tracks.size) { Long.MIN_VALUE }
            while (extractor.sampleTrackIndex >= 0) {
                val index = extractor.sampleTrackIndex
                sampleCounts[index]++
                val sampleTimeUs = extractor.sampleTime
                if (sampleTimeUs >= 0L) {
                    firstSampleTimesUs[index] = minOf(firstSampleTimesUs[index], sampleTimeUs)
                    lastSampleTimesUs[index] = maxOf(lastSampleTimesUs[index], sampleTimeUs)
                }
                if (!extractor.advance()) break
            }
            tracks.forEachIndexed { index, track ->
                track.sampleCount = sampleCounts[index]
                track.firstSampleTimeUs = firstSampleTimesUs[index].takeUnless { it == Long.MAX_VALUE }
                track.lastSampleTimeUs = lastSampleTimesUs[index].takeUnless { it == Long.MIN_VALUE }
            }
            val trackDurationUs = tracks.mapNotNull { it.durationUs }.maxOrNull() ?: 0L
            val durationUs = if (trackDurationUs > 0L) trackDurationUs else metadataDurationUs(file)
            val jsonTracks = JSONArray().apply { tracks.forEach { put(it.toJson()) } }
            return MediaInspection(tracks, (durationUs / 1000).toInt(), JSONObject()
                .put("file", file.name)
                .put("file_bytes", file.length())
                .put("duration_ms", durationUs / 1000)
                .put("tracks", jsonTracks))
        } finally {
            extractor.release()
        }
    }

    private fun metadataDurationUs(file: File): Long {
        val retriever = MediaMetadataRetriever()
        return try {
            retriever.setDataSource(file.absolutePath)
            (retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DURATION)?.toLongOrNull() ?: 0L) * 1000L
        } finally {
            retriever.release()
        }
    }

    private fun decodeAudioPlayback(file: File): JSONObject {
        val result = JSONObject().put("method", "MediaPlayer silent playback")
        val completion = CountDownLatch(1)
        val failure = AtomicReference<String?>(null)
        var player: MediaPlayer? = null
        try {
            player = MediaPlayer().apply {
                setDataSource(file.absolutePath)
                setVolume(0f, 0f)
                setOnCompletionListener { completion.countDown() }
                setOnErrorListener { _, what, extra ->
                    failure.set("MediaPlayer error what=$what extra=$extra")
                    completion.countDown()
                    true
                }
                prepare()
                result.put("prepared", true)
                start()
                result.put("started", true)
            }
            val completed = completion.await(MAX_DECODE_WAIT_MS, TimeUnit.MILLISECONDS) && failure.get() == null
            if (!completed && failure.get() == null) failure.set("Playback completion timed out after ${MAX_DECODE_WAIT_MS}ms")
            result.put("completed", completed)
        } catch (error: Exception) {
            failure.set("${error.javaClass.simpleName}: ${error.message}")
            result.put("completed", false)
        } finally {
            runCatching { player?.release() }
        }
        result.put("error", failure.get())
        return result
    }

    private fun decodeCircleFrame(file: File): JSONObject {
        val result = JSONObject().put("method", "MediaMetadataRetriever frame decode at 1s")
        val retriever = MediaMetadataRetriever()
        var frame: android.graphics.Bitmap? = null
        try {
            retriever.setDataSource(file.absolutePath)
            frame = retriever.getFrameAtTime(1_000_000L, MediaMetadataRetriever.OPTION_CLOSEST_SYNC)
            result.put("frame_decoded", frame != null)
            if (frame != null) {
                result.put("frame_width", frame.width)
                result.put("frame_height", frame.height)
            } else result.put("error", "No frame returned")
        } catch (error: Exception) {
            result.put("frame_decoded", false)
            result.put("error", "${error.javaClass.simpleName}: ${error.message}")
        } finally {
            frame?.recycle()
            retriever.release()
        }
        return result
    }

    private fun writeEvidence(kind: String, recorded: Recorded, media: MediaInspection, target: JSONObject) {
        val result = JSONObject()
            .put("kind", kind)
            .put("callback_duration_ms", recorded.durationMs)
            .put("target", target)
            .put("actual", media.json)
            .put("errors", JSONArray(errors))
        File(evidenceDirectory, "$kind.json").writeText(result.toString(2), Charsets.UTF_8)
    }

    private data class Recorded(val kind: String, val file: File, val durationMs: Int)
    private data class MediaInspection(val tracks: List<MediaTrack>, val durationMs: Int, val json: JSONObject)
    private data class MediaTrack(
        val mime: String,
        val channels: Int?,
        val sampleRate: Int?,
        val bitrate: Int?,
        val width: Int?,
        val height: Int?,
        val durationUs: Long?,
        var sampleCount: Int = 0,
        var firstSampleTimeUs: Long? = null,
        var lastSampleTimeUs: Long? = null
    ) {
        fun durationMs(): Long? {
            val formatDuration = durationUs?.takeIf { it > 0L }
            if (formatDuration != null) return formatDuration / 1000L
            val first = firstSampleTimeUs ?: return null
            val last = lastSampleTimeUs ?: return null
            return ((last - first).coerceAtLeast(0L)) / 1000L
        }

        fun toJson() = JSONObject()
            .put("mime", mime)
            .put("channels", channels)
            .put("sample_rate_hz", sampleRate)
            .put("bitrate_bps", bitrate)
            .put("width", width)
            .put("height", height)
            .put("duration_us", durationUs)
            .put("first_sample_time_us", firstSampleTimeUs)
            .put("last_sample_time_us", lastSampleTimeUs)
            .put("sample_span_duration_ms", durationMs())
            .put("extractor_demuxed_sample_count", sampleCount)
    }

    private fun MediaFormat.intOrNull(key: String): Int? = if (containsKey(key)) getInteger(key) else null
    private fun MediaFormat.longOrNull(key: String): Long? = if (containsKey(key)) getLong(key) else null

    companion object {
        private const val RECORDING_MILLIS = 4_000L
        private const val VOICE_MAX_BYTES = 4L * 1024 * 1024
        private const val CIRCLE_MAX_BYTES = 24L * 1024 * 1024
        private const val MIN_MEDIA_DURATION_MS = 2_500
        private const val MIN_CAPTURE_DURATION_MS = 2_500
        private const val MAX_DECODE_WAIT_MS = 30_000L
        private const val CIRCLE_TRACK_DELTA_TOLERANCE_MS = 250L
        private val CIRCLE_AUDIO_BITRATE_RANGE = 80_000..112_000
    }
}
