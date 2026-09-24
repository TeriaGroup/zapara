package ru.bgtu_voenmeh.zapara.ui.groups

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.accounts.testToken
import ru.bgtu_voenmeh.zapara.data.api.HttpCall
import ru.bgtu_voenmeh.zapara.data.api.HttpExchange
import ru.bgtu_voenmeh.zapara.data.api.HttpReply
import ru.bgtu_voenmeh.zapara.data.communities.CommunityHttpClient
import ru.bgtu_voenmeh.zapara.data.communities.CommunityClientException
import ru.bgtu_voenmeh.zapara.data.communities.CommunityClientFailure

class GroupMediaTest {
    @Test
    fun downloadedVoiceAndCircleUseExtensionsThatMatchTheWireFormat() {
        val webm = byteArrayOf(0x1A, 0x45, 0xDF.toByte(), 0xA3.toByte())
        val ogg = "OggS".toByteArray()
        val mp4 = byteArrayOf(0, 0, 0, 12) + "ftyp".toByteArray() + "isom".toByteArray()
        assertEquals(".webm", GroupMedia.extension("voice", webm))
        assertEquals(".ogg", GroupMedia.extension("voice", ogg))
        assertEquals(".m4a", GroupMedia.extension("voice", mp4))
        assertEquals(".webm", GroupMedia.extension("circle", webm))
        assertEquals(".mp4", GroupMedia.extension("circle", mp4))
    }

    @Test
    fun recordedVoiceAndCircleCarryDurationWithSeparateSizeLimits() = runBlocking {
        val calls = mutableListOf<HttpCall>()
        val transport = HttpExchange { call ->
            calls += call
            val kind = call.headers.getValue("X-Zapara-Kind")
            HttpReply(201, """{"messageId":"11111111-1111-4111-8111-111111111111","conversationId":"22222222-2222-4222-8222-222222222222","senderId":"33333333-3333-4333-8333-333333333333","senderName":"Аня","body":"$kind","createdAt":"2026-09-23T12:00:00Z","kind":"$kind","deleted":false}""".toByteArray())
        }
        val client = CommunityHttpClient(transport, AccountServerScope.parse("http://127.0.0.1:9/"))
        val token = testToken("za_", 7)
        val id = "22222222-2222-4222-8222-222222222222"
        val voice = byteArrayOf(0, 0, 0, 12, 'f'.code.toByte(), 't'.code.toByte(), 'y'.code.toByte(), 'p'.code.toByte(), 0, 0, 0, 0)
        assertEquals("voice", GroupMedia.place(client, token, id, "voice", "voice.m4a", voice, null, 4200).kind)
        assertEquals("4200", calls.single().headers["X-Zapara-Duration-Ms"])
        assertTrue(calls.single().body!!.contentEquals(voice))
        val circle = voice + byteArrayOf(1, 2, 3)
        assertEquals("circle", GroupMedia.place(client, token, id, "circle", "circle.mp4", circle, null, 12000).kind)
        assertEquals("12000", calls.last().headers["X-Zapara-Duration-Ms"])
        try {
            GroupMedia.place(client, token, id, "voice", "missing-duration.m4a", voice, null)
            fail("Voice without a declared duration must be rejected")
        } catch (expected: CommunityClientException) {
            assertEquals(CommunityClientFailure.InvalidRequest, expected.failure)
        }
        try {
            GroupMedia.place(client, token, id, "voice", "too-long.m4a", ByteArray(2 * 1024 * 1024 + 1), null, 1000)
            fail("Voice over 2 MiB must be rejected before upload")
        } catch (expected: CommunityClientException) {
            assertEquals(CommunityClientFailure.PayloadTooLarge, expected.failure)
        }
        assertEquals(2, calls.size)
    }

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
