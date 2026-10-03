package ru.bgtu_voenmeh.zapara.ui.inbox

import java.time.Instant
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Assert.assertSame
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.social.InboxRow
import ru.bgtu_voenmeh.zapara.data.social.SocialMessage
import ru.bgtu_voenmeh.zapara.data.social.SocialFailure

class PersonalMediaLoadGuardTest {
    private val message = SocialMessage(
        id = "message-a", senderId = "sender", senderName = "Sender", body = null,
        kind = "image", createdAt = Instant.EPOCH, replyTo = null, replyBody = null,
        deleted = false, edited = false, read = true, reactions = emptyList(),
        attachmentId = "attachment-a", fileName = "photo.jpg")

    @Test fun queued_media_tap_is_rejected_after_chat_switch_or_history_removal() {
        val currentChat = InboxUiState(active = InboxRow("chat-a", "Chat A"), messages = listOf(message))
        assertTrue(personalMediaRequestIsCurrent(currentChat, "chat-a", message))
        assertFalse(personalMediaRequestIsCurrent(currentChat.copy(active = InboxRow("chat-b", "Chat B")), "chat-a", message))
        assertFalse(personalMediaRequestIsCurrent(currentChat.copy(messages = emptyList()), "chat-a", message))
    }

    @Test fun replaced_or_deleted_attachment_message_cannot_publish_old_media() {
        val changed = message.copy(attachmentId = "attachment-b")
        val deleted = message.copy(deleted = true)
        assertFalse(personalMediaRequestIsCurrent(InboxUiState(
            active = InboxRow("chat-a", "Chat A"), messages = listOf(changed)), "chat-a", message))
        assertFalse(personalMediaRequestIsCurrent(InboxUiState(
            active = InboxRow("chat-a", "Chat A"), messages = listOf(deleted)), "chat-a", message))
    }

    @Test fun only_server_404_marks_a_personal_attachment_unavailable() {
        assertTrue(personalMediaMissingOnServer(SocialFailure(404)))
        assertFalse(personalMediaMissingOnServer(SocialFailure(401)))
        assertFalse(personalMediaMissingOnServer(SocialFailure(503)))
        assertFalse(personalMediaMissingOnServer(IllegalStateException("local cache failure")))
    }

    @Test fun failure_status_is_not_published_after_chat_switch_or_message_deletion() {
        val current = InboxUiState(active = InboxRow("chat-a", "Chat A"), messages = listOf(message),
            mediaLoading = setOf("attachment-a"))
        val missing = personalMediaFailureState(current, "chat-a", message, SocialFailure(404))
        assertTrue("attachment-a" in missing.mediaNotFound && "attachment-a" in missing.mediaErrors)
        assertFalse("attachment-a" in missing.mediaLoading)
        val otherChat = current.copy(active = InboxRow("chat-b", "Chat B"))
        assertSame(otherChat, personalMediaFailureState(otherChat, "chat-a", message, SocialFailure(404)))
        val deleted = current.copy(messages = listOf(message.copy(deleted = true)))
        assertSame(deleted, personalMediaFailureState(deleted, "chat-a", message, SocialFailure(404)))
        val transient = personalMediaFailureState(missing, "chat-a", message, SocialFailure(503))
        assertFalse("attachment-a" in transient.mediaNotFound)
        assertTrue("attachment-a" in transient.mediaErrors)
    }

    @Test fun navigation_clears_failure_flags_without_reusing_old_conversation_status() {
        val old = InboxUiState(active = InboxRow("chat-a", "Chat A"), messages = listOf(message),
            mediaLoading = setOf("attachment-a"), mediaErrors = setOf("attachment-a"),
            mediaNotFound = setOf("attachment-a"))
        val clean = old.withoutMediaStatus()
        assertTrue(clean.mediaLoading.isEmpty() && clean.mediaErrors.isEmpty() && clean.mediaNotFound.isEmpty())
        assertTrue(clean.messages == old.messages && clean.active == old.active)
    }
}
