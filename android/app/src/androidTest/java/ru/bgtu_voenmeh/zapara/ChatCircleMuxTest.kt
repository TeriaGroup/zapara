package ru.bgtu_voenmeh.zapara

import android.media.MediaExtractor
import android.media.MediaFormat
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import ru.bgtu_voenmeh.zapara.ui.media.ChatCircleMux
import java.io.File
import java.nio.ByteBuffer
import java.security.MessageDigest
import kotlin.math.abs

@RunWith(AndroidJUnit4::class)
class ChatCircleMuxTest {
    // Fixture: ffmpeg -f lavfi -i color=c=black:s=32x32:r=2:d=2
    // -f lavfi -i anullsrc=r=48000:cl=mono -t 2 -c:v libx264 -profile:v baseline
    // -bf 0 -g 1 -c:a aac -b:a 96k -use_editlist 0 -movflags +faststart circle-sparse-frames.mp4
    @Test fun sparse_video_keeps_its_last_frame_duration_and_encoded_samples() = withFixture { source, output ->
        ChatCircleMux.merge(source, source, output, audioDelayUs = 0L) {}

        val originalVideo = inspect(source, "video/")
        val video = inspect(output, "video/")
        val audio = inspect(output, "audio/")
        // Without an edit list, AAC priming adds 21.362 ms to the fixture's
        // second and subsequent video PTS. Its final PTS is 1,521,362 us.
        // End that frame after 100 ms, not the preceding 500 ms interval.
        assertTrue("Final frame must end at 1.621s, not repeat the preceding 500ms gap: $video",
            abs(video.durationUs - 1_621_362L) <= 1_000L)
        assertEquals("Video sample bytes must survive remuxing", originalVideo.hashes, video.hashes)
        assertEquals(originalVideo.timesUs.size, video.timesUs.size)
        originalVideo.timesUs.zip(video.timesUs).forEach { (before, after) ->
            // Android MP4 video timestamps quantize to a 90 kHz timescale
            // (one tick is 11.112 us); allow only that rounding, not drift.
            assertTrue("Video PTS changed beyond one muxer tick: $before -> $after", abs(before - after) <= 12L)
        }
        assertTrue("Track ends must align within 250 ms: video=$video audio=$audio",
            abs(video.durationUs - audio.durationUs) <= 250_000L)
        assertEquals(48_000, audio.format.getInteger(MediaFormat.KEY_SAMPLE_RATE))
        assertEquals(1, audio.format.getInteger(MediaFormat.KEY_CHANNEL_COUNT))
        val originalAudio = inspect(source, "audio/")
        assertEquals("AAC sample bytes must survive remuxing", originalAudio.hashes.take(audio.hashes.size), audio.hashes)
    }

    @Test fun mux_preserves_audio_start_offset_instead_of_hiding_it_by_shifting_samples() = withFixture { source, output ->
        ChatCircleMux.merge(source, source, output, audioDelayUs = 400_000L) {}
        val audio = inspect(output, "audio/")
        assertEquals(400_000L, audio.timesUs.first())
        val original = inspect(source, "audio/")
        assertEquals(original.hashes.take(audio.hashes.size), audio.hashes)
        assertEquals(original.timesUs.take(audio.timesUs.size).map { it - original.timesUs.first() + 400_000L }, audio.timesUs)
    }

    private fun withFixture(test: (File, File) -> Unit) {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val source = File.createTempFile("circle-mux-fixture-", ".mp4", instrumentation.targetContext.cacheDir)
        val output = File.createTempFile("circle-mux-result-", ".mp4", instrumentation.targetContext.cacheDir)
        try {
            instrumentation.context.assets.open("media/circle-sparse-frames.mp4").use { input ->
                source.outputStream().use { input.copyTo(it) }
            }
            for (prefix in listOf("video/", "audio/")) {
                val track = inspect(source, prefix)
                assertTrue("Fixture $prefix must expose nonnegative Android extractor PTS", track.timesUs.isNotEmpty())
                assertEquals("Fixture $prefix must begin at zero", 0L, track.timesUs.first())
            }
            test(source, output)
        } finally {
            source.delete()
            output.delete()
        }
    }

    private data class Track(val format: MediaFormat, val durationUs: Long, val timesUs: List<Long>, val hashes: List<String>)

    private fun inspect(file: File, prefix: String): Track {
        val extractor = MediaExtractor()
        try {
            extractor.setDataSource(file.absolutePath)
            val index = (0 until extractor.trackCount).single {
                extractor.getTrackFormat(it).getString(MediaFormat.KEY_MIME)?.startsWith(prefix) == true
            }
            val format = extractor.getTrackFormat(index)
            extractor.selectTrack(index)
            val times = mutableListOf<Long>()
            val hashes = mutableListOf<String>()
            val buffer = ByteBuffer.allocate(1024 * 1024)
            while (extractor.sampleTime >= 0) {
                buffer.clear()
                val size = extractor.readSampleData(buffer, 0)
                check(size > 0)
                times += extractor.sampleTime
                val bytes = ByteArray(size)
                buffer.position(0)
                buffer.get(bytes)
                hashes += MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02x".format(it) }
                if (!extractor.advance()) break
            }
            return Track(format, format.getLong(MediaFormat.KEY_DURATION), times, hashes)
        } finally {
            extractor.release()
        }
    }
}
