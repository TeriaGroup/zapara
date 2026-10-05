package ru.bgtu_voenmeh.zapara.ui.groups

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class GroupHoldTest {
    @Test
    fun holdCallsTheMessengerAndAPhotoCannotBeEdited() = runBlocking {
        val calls = mutableListOf<String>()
        val api = object : GroupHoldApi {
            override suspend fun edit(token: String, conversationId: String, messageId: String, body: String) {
                calls += "edit:$messageId"
            }
            override suspend fun delete(token: String, conversationId: String, messageId: String) {
                calls += "delete:$messageId"
            }
            override suspend fun react(token: String, conversationId: String, messageId: String, emoji: String) {
                calls += "react:$messageId:$emoji"
            }
        }
        val text = GroupMessageUi("m1", "Аня", "текст", "сейчас", true, "text", false)
        val photo = GroupMessageUi("m2", "Аня", "фото", "сейчас", true, "image", false)
        val replied = GroupHold.perform(text, "reply", "c", "t", api, GroupHoldState("черновик"))
        assertEquals("m1", replied.replyTo)
        assertEquals("", replied.draft)
        assertNull(replied.editing)
        val edited = GroupHold.perform(text, "edit", "c", "t", api, GroupHoldState())
        assertEquals("m1", edited.editing)
        assertEquals("текст", edited.draft)
        GroupHold.perform(text, "reaction", "c", "t", api, GroupHoldState())
        val removed = GroupHold.perform(photo, "delete", "c", "t", api, GroupHoldState())
        assertTrue(removed.removed)
        val refused = GroupHold.perform(photo, "edit", "c", "t", api, GroupHoldState("остаётся"))
        assertEquals("остаётся", refused.draft)
        assertNull(refused.editing)
        assertEquals(listOf("react:m1:like", "delete:m2"), calls)
    }
}
