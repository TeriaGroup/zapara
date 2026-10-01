package ru.bgtu_voenmeh.zapara.ui.media

import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Test

class ChatRecordingReviewTest {
    @Test fun send_takes_the_exact_reviewed_file_and_transfers_ownership_once() {
        val file = File.createTempFile("reviewed-voice-", ".m4a").apply { writeBytes(byteArrayOf(1, 2, 3)) }
        val review = ChatRecordingReview()
        val recording = ReviewedChatRecording("voice", file, 1500)
        try {
            review.preview(recording)
            val sent = review.takeForSend()
            assertSame(file, sent?.file)
            assertEquals(recording, sent)
            assertTrue(file.isFile)
            assertNull(review.takeForSend())
        } finally { file.delete() }
    }

    @Test fun discard_and_replacing_review_delete_only_the_superseded_temporary_file() {
        val oldFile = File.createTempFile("old-review-", ".m4a").apply { writeBytes(byteArrayOf(1)) }
        val newFile = File.createTempFile("new-review-", ".m4a").apply { writeBytes(byteArrayOf(2)) }
        val review = ChatRecordingReview()
        try {
            review.preview(ReviewedChatRecording("voice", oldFile, 1000))
            review.preview(ReviewedChatRecording("voice", newFile, 2000))
            assertFalse(oldFile.exists())
            assertTrue(newFile.isFile)

            review.discard()

            assertFalse(newFile.exists())
            assertNull(review.current)
        } finally { review.discard(); oldFile.delete(); newFile.delete() }
    }
}
