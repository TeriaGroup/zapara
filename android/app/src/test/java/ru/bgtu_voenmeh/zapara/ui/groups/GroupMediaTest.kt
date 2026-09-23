package ru.bgtu_voenmeh.zapara.ui.groups

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.accounts.testToken
import ru.bgtu_voenmeh.zapara.data.api.HttpCall
import ru.bgtu_voenmeh.zapara.data.api.HttpExchange
import ru.bgtu_voenmeh.zapara.data.api.HttpReply
import ru.bgtu_voenmeh.zapara.data.communities.CommunityHttpClient

class GroupMediaTest {
    @Test
    fun placeSendsTheFileAndAPhotoCanBeHeldButNotEdited() = runBlocking {
        val calls = mutableListOf<HttpCall>()
        val transport = HttpExchange { call ->
            calls += call
            val kind = call.headers.getValue("X-Zapara-Kind")
            val body = when (kind) {
                "image" -> "Фото"
                "video" -> "Видео"
                else -> "notes.txt"
            }
            HttpReply(201, """{"messageId":"11111111-1111-4111-8111-111111111111","conversationId":"22222222-2222-4222-8222-222222222222","senderId":"33333333-3333-4333-8333-333333333333","senderName":"Аня","body":"$body","createdAt":"2026-09-23T12:00:00Z","kind":"$kind","deleted":false}""".toByteArray())
        }
        val client = CommunityHttpClient(transport, AccountServerScope.parse("http://127.0.0.1:9/"))
        val photo = byteArrayOf(1, 2, 3, 4)
        val saved = GroupMedia.place(client, testToken("za_", 7), "22222222-2222-4222-8222-222222222222", "image", "папка/снимок.png", photo, null)
        assertEquals("image", saved.kind)
        assertEquals("Фото", saved.body)
        val call = calls.single()
        assertTrue(java.net.URLDecoder.decode(call.headers.getValue("X-Zapara-Name"), "UTF-8").endsWith("снимок.png"))
        assertTrue(call.url.endsWith("/conversations/22222222-2222-4222-8222-222222222222/media"))
        assertEquals("application/octet-stream", call.headers["Content-Type"])
        assertEquals("image", call.headers["X-Zapara-Kind"])
        assertTrue(call.body!!.contentEquals(photo))
        val bubble = GroupMessageUi(saved.messageId, saved.senderName, saved.body, "сейчас", true, saved.kind, saved.deleted)
        val hold = mutableListOf<String>()
        val api = object : GroupHoldApi {
            override suspend fun edit(token: String, conversationId: String, messageId: String, body: String) { hold += "edit" }
            override suspend fun delete(token: String, conversationId: String, messageId: String) { hold += "delete" }
            override suspend fun react(token: String, conversationId: String, messageId: String, emoji: String) { hold += "react:$emoji" }
        }
        val replied = GroupHold.perform(bubble, "reply", "c", "t", api, GroupHoldState())
        assertEquals(saved.messageId, replied.replyTo)
        val refused = GroupHold.perform(bubble, "edit", "c", "t", api, GroupHoldState("черновик"))
        assertEquals("черновик", refused.draft)
        assertNull(refused.editing)
        GroupHold.perform(bubble, "reaction", "c", "t", api, GroupHoldState())
        val removed = GroupHold.perform(bubble, "delete", "c", "t", api, GroupHoldState())
        assertTrue(removed.removed)
        assertEquals(listOf("react:like", "delete"), hold)
        val clipBytes = byteArrayOf(9, 8, 7)
        val clip = GroupMedia.place(client, testToken("za_", 7), "22222222-2222-4222-8222-222222222222", "video", "ролик.mp4", clipBytes, null)
        assertEquals("video", clip.kind)
        assertTrue(calls[1].body!!.contentEquals(clipBytes))
        assertEquals("video", calls[1].headers["X-Zapara-Kind"])
        val clipBubble = GroupMessageUi(clip.messageId, clip.senderName, clip.body, "сейчас", true, clip.kind, clip.deleted)
        val clipHold = mutableListOf<String>()
        val clipApi = object : GroupHoldApi {
            override suspend fun edit(token: String, conversationId: String, messageId: String, body: String) { clipHold += "edit" }
            override suspend fun delete(token: String, conversationId: String, messageId: String) { clipHold += "delete" }
            override suspend fun react(token: String, conversationId: String, messageId: String, emoji: String) { clipHold += "react" }
        }
        GroupHold.perform(clipBubble, "edit", "c", "t", clipApi, GroupHoldState())
        GroupHold.perform(clipBubble, "reaction", "c", "t", clipApi, GroupHoldState())
        val clipGone = GroupHold.perform(clipBubble, "delete", "c", "t", clipApi, GroupHoldState())
        assertTrue(clipGone.removed)
        assertEquals(listOf("react", "delete"), clipHold)
        val notesBytes = byteArrayOf(5)
        val notes = GroupMedia.place(client, testToken("za_", 7), "22222222-2222-4222-8222-222222222222", "file", "notes.txt", notesBytes, null)
        assertEquals("file", notes.kind)
        assertTrue(calls[2].body!!.contentEquals(notesBytes))
        assertEquals("file", calls[2].headers["X-Zapara-Kind"])
    }
}
