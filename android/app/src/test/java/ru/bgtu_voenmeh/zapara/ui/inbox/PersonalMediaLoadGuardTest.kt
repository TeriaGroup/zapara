package ru.bgtu_voenmeh.zapara.ui.inbox

import java.time.Instant
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.social.InboxRow
import ru.bgtu_voenmeh.zapara.data.social.SocialMessage

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
}
