package ru.bgtu_voenmeh.zapara.ui.inbox

import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Test

class PendingPersonalRecordingStoreTest {
    private fun recording(owner: String, chat: String, file: File) = PendingPersonalRecording(
        PersonalRecordingScope(owner, "db-$owner", chat), "voice", file, 1500)

    @Test fun failed_upload_remains_retryable_with_the_same_file_only_for_its_owner_and_chat() {
        val file = File.createTempFile("pending-recording-", ".m4a").apply { writeBytes(byteArrayOf(1, 2, 3)) }
        val store = PendingPersonalRecordingStore()
        val first = recording("profile-a", "chat-a", file)
        try {
            assertTrue(store.begin(first))
            assertTrue(store.markUncertain(first.scope, file))
            val pending = store.get(first.scope)!!
            assertTrue(pending.uncertain)
            assertFalse(pending.inFlight)
            assertTrue(file.isFile)

            val retry = store.retry(first.scope)!!
            assertSame(file, retry.file)
            assertTrue(retry.inFlight)
            assertTrue(retry.uncertain)
            assertTrue(store.markUncertain(first.scope, retry.file))
            assertSame(file, store.get(first.scope)?.file)
            assertTrue(file.isFile)
            assertNull(store.get(PersonalRecordingScope("profile-b", "db-profile-b", "chat-a")))
            assertNull(store.get(PersonalRecordingScope("profile-a", "db-profile-a", "chat-b")))
        } finally {
            store.clearAndDelete()
            file.delete()
        }
    }

    @Test fun a_new_capture_cannot_replace_a_pending_file_and_discard_is_exact() {
        val firstFile = File.createTempFile("first-recording-", ".m4a").apply { writeBytes(byteArrayOf(1)) }
        val nextFile = File.createTempFile("next-recording-", ".m4a").apply { writeBytes(byteArrayOf(2)) }
        val store = PendingPersonalRecordingStore()
        val first = recording("profile-a", "chat-a", firstFile)
        try {
            assertTrue(store.begin(first))
            assertTrue(store.markUncertain(first.scope, firstFile))
            assertFalse(store.begin(recording("profile-a", "chat-a", nextFile)))
            assertSame(firstFile, store.get(first.scope)?.file)
            assertTrue(nextFile.isFile)
            assertFalse(store.discard(first.scope, nextFile))
            assertTrue(store.discard(first.scope, firstFile))
            assertFalse(firstFile.exists())
            assertTrue(nextFile.isFile)
        } finally {
            store.clearAndDelete()
            firstFile.delete()
            nextFile.delete()
        }
    }

    @Test fun acknowledged_and_canceled_owned_files_are_deleted_but_failed_files_are_kept() {
        val failedFile = File.createTempFile("failed-recording-", ".m4a").apply { writeBytes(byteArrayOf(1)) }
        val acceptedFile = File.createTempFile("accepted-recording-", ".m4a").apply { writeBytes(byteArrayOf(2)) }
        val cancelledFile = File.createTempFile("cancelled-recording-", ".m4a").apply { writeBytes(byteArrayOf(3)) }
        val store = PendingPersonalRecordingStore()
        val failed = recording("profile-a", "chat-a", failedFile)
        val accepted = recording("profile-a", "chat-b", acceptedFile)
        val cancelled = recording("profile-a", "chat-c", cancelledFile)
        try {
            store.begin(failed)
            store.markUncertain(failed.scope, failedFile)
            assertTrue(failedFile.isFile)
            store.begin(accepted)
            assertTrue(store.removeAccepted(accepted.scope, acceptedFile))
            assertNull(store.get(accepted.scope))
            store.begin(cancelled)
            assertTrue(store.removeCancelled(cancelled.scope, cancelledFile))
            assertFalse(cancelledFile.exists())
        } finally {
            store.clearAndDelete()
            failedFile.delete()
            acceptedFile.delete()
            cancelledFile.delete()
        }
    }
}
