package ru.bgtu_voenmeh.zapara.ui.groups

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class GroupMediaSaveTest {
    private val target = GroupMediaSaveTarget("owner-a", "community-a", "conversation-a", "topic-a", "message-a")
    private val message = GroupMessageUi("message-a", "Анна", "лекция.pdf", "10:00", false, kind = "file")

    @Test fun attachment_save_requires_the_captured_owner_conversation_topic_and_message() {
        assertTrue(canSaveGroupAttachment("owner-a", "community-a", "conversation-a", "topic-a", target, message))
        assertTrue(canSaveGroupAttachment("owner-a", "community-a", "conversation-a", "topic-a", target,
            message.copy(kind = "voice")))
        assertFalse(canSaveGroupAttachment("owner-b", "community-a", "conversation-a", "topic-a", target, message))
        assertFalse(canSaveGroupAttachment("owner-a", "community-b", "conversation-a", "topic-a", target, message))
        assertFalse(canSaveGroupAttachment("owner-a", "community-a", "conversation-b", "topic-a", target, message))
        assertFalse(canSaveGroupAttachment("owner-a", "community-a", "conversation-a", "topic-b", target, message))
        assertFalse(canSaveGroupAttachment("owner-a", "community-a", "conversation-a", "topic-a", target, message.copy(deleted = true)))
        assertFalse(canSaveGroupAttachment("owner-a", "community-a", "conversation-a", "topic-a", target, message.copy(kind = "text")))
    }

    @Test fun suggested_filename_keeps_the_leaf_extension_and_never_exposes_a_path() {
        assertEquals("notes final.pdf", groupAttachmentSuggestedName("C:\\download\\notes final.pdf"))
        assertEquals("attachment", groupAttachmentSuggestedName("../"))
    }
}
