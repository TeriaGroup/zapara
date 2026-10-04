package ru.bgtu_voenmeh.zapara.ui.media

import android.media.MediaCodec
import android.media.MediaExtractor
import android.media.MediaFormat
import android.media.MediaMetadataRetriever
import android.media.MediaMuxer
import java.io.File
import java.io.IOException
import java.nio.ByteBuffer

/** Copies encoded camera video and AAC microphone samples into one MP4 without re-encoding. */
internal object ChatCircleMux {
    private const val maxVideoBytes = 24L * 1024 * 1024
    private const val maxAudioBytes = 4L * 1024 * 1024

    fun merge(video: File, audio: File, output: File, audioDelayUs: Long, checkActive: () -> Unit) {
        if (video.length() !in 1000..maxVideoBytes || audio.length() !in 200..maxAudioBytes || audioDelayUs < 0)
            throw IOException("Invalid circle tracks")
        val videoInput = MediaExtractor()
        val audioInput = MediaExtractor()
        var muxer: MediaMuxer? = null
        var muxerStarted = false
        try {
            videoInput.setDataSource(video.absolutePath)
            audioInput.setDataSource(audio.absolutePath)
            val (videoIndex, videoFormat) = track(videoInput, "video/")
            val (audioIndex, audioFormat) = track(audioInput, "audio/")
            if (audioFormat.getString(MediaFormat.KEY_MIME) != MediaFormat.MIMETYPE_AUDIO_AAC ||
                !audioFormat.containsKey(MediaFormat.KEY_SAMPLE_RATE) ||
                audioFormat.getInteger(MediaFormat.KEY_SAMPLE_RATE) < 44_100 ||
                !audioFormat.containsKey(MediaFormat.KEY_CHANNEL_COUNT) ||
                audioFormat.getInteger(MediaFormat.KEY_CHANNEL_COUNT) != 1)
                throw IOException("Circle audio is not mono AAC at 44.1 kHz or better")

            val (videoFirstUs, videoEndUs) = videoTimes(video, videoIndex, checkActive)
            videoInput.selectTrack(videoIndex)
            audioInput.selectTrack(audioIndex)
            val audioFirstUs = audioInput.sampleTime
            if (audioFirstUs < 0) throw IOException("Circle audio has no samples")
            val rotation = rotation(video)
            val outputMuxer = MediaMuxer(output.absolutePath, MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4)
            muxer = outputMuxer
            val videoOutput = outputMuxer.addTrack(videoFormat)
            val audioOutput = outputMuxer.addTrack(audioFormat)
            outputMuxer.setOrientationHint(rotation)
            outputMuxer.start()
            muxerStarted = true

            val buffer = ByteBuffer.allocateDirect(4 * 1024 * 1024)
            val info = MediaCodec.BufferInfo()
            var copiedVideo = 0
            var copiedAudio = 0
            while (true) {
                checkActive()
                val videoPts = videoInput.sampleTime.takeIf { it >= 0 }?.minus(videoFirstUs)
                val audioPts = audioInput.sampleTime.takeIf { it >= 0 }
                    ?.let { it - audioFirstUs + audioDelayUs }
                    ?.takeIf { it in 0L..videoEndUs }
                if (videoPts == null && audioPts == null) break
                if (videoPts != null && (audioPts == null || videoPts <= audioPts)) {
                    writeSample(videoInput, outputMuxer, videoOutput, videoPts, buffer, info)
                    copiedVideo++
                    videoInput.advance()
                } else {
                    writeSample(audioInput, outputMuxer, audioOutput, audioPts!!, buffer, info)
                    copiedAudio++
                    audioInput.advance()
                }
            }
            if (copiedVideo == 0 || copiedAudio == 0) throw IOException("Circle has an empty track")
            // Without explicit EOS, MediaMuxer repeats the preceding frame interval
            // for the last frame. At low FPS that exceeds the endpoint used to trim AAC.
            buffer.clear()
            info.set(0, 0, videoEndUs, MediaCodec.BUFFER_FLAG_END_OF_STREAM)
            outputMuxer.writeSampleData(videoOutput, buffer, info)
            outputMuxer.stop()
            muxerStarted = false
            if (output.length() !in 1000..maxVideoBytes) throw IOException("Circle exceeds media limit")
        } finally {
            if (muxerStarted) runCatching { muxer?.stop() }
            runCatching { muxer?.release() }
            videoInput.release()
            audioInput.release()
        }
    }

    private fun track(input: MediaExtractor, prefix: String): Pair<Int, MediaFormat> {
        for (index in 0 until input.trackCount) {
            val format = input.getTrackFormat(index)
            if (format.getString(MediaFormat.KEY_MIME)?.startsWith(prefix) == true) return index to format
        }
        throw IOException("Circle track is missing: $prefix")
    }

    private fun videoTimes(file: File, trackIndex: Int, checkActive: () -> Unit): Pair<Long, Long> {
        val probe = MediaExtractor()
        try {
            probe.setDataSource(file.absolutePath)
            probe.selectTrack(trackIndex)
            val first = probe.sampleTime
            if (first < 0) throw IOException("Circle video has no samples")
            var last = first
            var previous = first
            while (probe.sampleTime >= 0) {
                checkActive()
                val sample = probe.sampleTime
                if (sample > last) {
                    previous = last
                    last = sample
                }
                if (!probe.advance()) break
            }
            if (last <= first) throw IOException("Circle video is too short")
            val finalFrameUs = (last - previous).coerceIn(10_000L, 100_000L)
            return first to (last - first + finalFrameUs)
        } finally { probe.release() }
    }

    private fun rotation(file: File): Int {
        val retriever = MediaMetadataRetriever()
        return try {
            retriever.setDataSource(file.absolutePath)
            retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_VIDEO_ROTATION)
                ?.toIntOrNull()?.takeIf { it == 0 || it == 90 || it == 180 || it == 270 } ?: 0
        } finally { retriever.release() }
    }

    private fun writeSample(input: MediaExtractor, output: MediaMuxer, track: Int, pts: Long,
                            buffer: ByteBuffer, info: MediaCodec.BufferInfo) {
        buffer.clear()
        val size = input.readSampleData(buffer, 0)
        if (size <= 0 || size > buffer.capacity()) throw IOException("Invalid circle sample")
        info.set(0, size, pts, input.sampleFlags)
        output.writeSampleData(track, buffer, info)
    }
}
