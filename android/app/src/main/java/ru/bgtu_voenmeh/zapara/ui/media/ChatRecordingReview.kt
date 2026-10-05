package ru.bgtu_voenmeh.zapara.ui.media

import java.io.File

internal data class ReviewedChatRecording(val kind: String, val file: File, val durationMs: Int)

internal class ChatRecordingReview {
    var current: ReviewedChatRecording? = null
        private set

    fun preview(recording: ReviewedChatRecording) {
        discard()
        current = recording
    }

    fun takeForSend(): ReviewedChatRecording? = current.also { current = null }

    fun discard() {
        current?.file?.delete()
        current = null
    }
}
