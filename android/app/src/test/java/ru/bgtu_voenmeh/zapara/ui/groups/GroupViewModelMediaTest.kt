package ru.bgtu_voenmeh.zapara.ui.groups

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.accounts.testToken
import ru.bgtu_voenmeh.zapara.data.api.FakeHttp
import ru.bgtu_voenmeh.zapara.data.api.HttpReply
import ru.bgtu_voenmeh.zapara.data.api.jsonReply
import ru.bgtu_voenmeh.zapara.data.communities.CommunityHttpClient

@OptIn(ExperimentalCoroutinesApi::class)
class GroupViewModelMediaTest {
    private val dispatcher = StandardTestDispatcher()
    private val community = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
    private val conversation = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
    private val user = "cccccccc-cccc-4ccc-8ccc-cccccccccccc"

    @Before fun setMain() { Dispatchers.setMain(dispatcher) }
    @After fun reset() { Dispatchers.resetMain() }

    @Test
    fun sendPostsPhotoVideoAndFileThroughTheOpenChat() = runTest(dispatcher) {
        val http = FakeHttp { call ->
            val path = call.url.substringAfter("/communities").substringBefore('?')
            when {
                call.method == "GET" && !path.contains("/") -> jsonReply("""[{"communityId":"$community","name":"O3313","description":"Группа","revision":1,"role":"member"}]""")
                path.endsWith("/home") -> jsonReply(home())
                path.endsWith("/messages") -> jsonReply("""{"messages":[],"hasMore":false}""")
                path.endsWith("/read") -> jsonReply(chat())
                path.endsWith("/media") -> HttpReply(201, message(call.headers.getValue("X-Zapara-Kind")).toByteArray())
                path.endsWith("/react") -> jsonReply(message("image"))
                path.endsWith("/delete") -> jsonReply(message("image", deleted = true))
                path.endsWith("/edit") -> jsonReply(message("image"))
                else -> HttpReply(404, """{"title":"Нет","status":404,"code":"not_found"}""".toByteArray())
            }
        }
        val vm = GroupViewModel(GroupRuntime(false, user, CommunityHttpClient(http, AccountServerScope.parse("http://127.0.0.1:9/")), { testToken("za_", 4) }, { null }))
        runCurrent()
        assertTrue(vm.state.value.hasHome)
        val photo = byteArrayOf(1, 2, 3, 4)
        val clip = byteArrayOf(9, 8, 7)
        val notes = byteArrayOf(5, 6)
        vm.onEvent(GroupEvent.Media("image", "снимок.png", photo))
        runCurrent()
        vm.onEvent(GroupEvent.Media("video", "ролик.mp4", clip))
        runCurrent()
        vm.onEvent(GroupEvent.Media("file", "notes.txt", notes))
        runCurrent()
        assertEquals(listOf("image", "video", "file"), vm.state.value.messages.map { it.kind })
        val uploads = http.requests.filter { it.url.endsWith("/media") }
        assertEquals(3, uploads.size)
        assertTrue(uploads[0].body!!.contentEquals(photo))
        assertEquals("image", uploads[0].headers["X-Zapara-Kind"])
        assertTrue(uploads[1].body!!.contentEquals(clip))
        assertEquals("video", uploads[1].headers["X-Zapara-Kind"])
        assertTrue(uploads[2].body!!.contentEquals(notes))
        assertEquals("file", uploads[2].headers["X-Zapara-Kind"])
        assertEquals("application/octet-stream", uploads[0].headers["Content-Type"])
        val photoId = vm.state.value.messages[0].id
        vm.onEvent(GroupEvent.Hold(photoId, "edit"))
        runCurrent()
        assertNull(vm.state.value.editing)
        assertFalse(http.requests.any { it.url.endsWith("/edit") })
        vm.onEvent(GroupEvent.Hold(photoId, "reply"))
        runCurrent()
        assertEquals(photoId, vm.state.value.replyTo)
        vm.onEvent(GroupEvent.Hold(photoId, "reaction"))
        runCurrent()
        assertTrue(http.requests.any { it.url.endsWith("/react") })
        vm.onEvent(GroupEvent.Hold(photoId, "delete"))
        runCurrent()
        assertTrue(vm.state.value.messages.first { it.id == photoId }.deleted)
        assertTrue(http.requests.any { it.url.endsWith("/delete") })
        vm.onEvent(GroupEvent.Back)
        runCurrent()
    }

    private fun home() = """{"communityId":"$community","name":"O3313","groupName":"O3313","groupChat":${chat()},"classmates":[{"userId":"$user","username":"student","displayName":"Аня","role":"member","self":true}],"directs":[]}"""

    private fun chat() = """{"conversationId":"$conversation","kind":"group","communityId":"$community","title":"O3313","peerUserId":null,"lastBody":null,"lastAt":null,"unread":0}"""

    private fun message(kind: String, deleted: Boolean = false): String {
        val body = if (kind == "image") "Фото" else if (kind == "video") "Видео" else "notes.txt"
        val id = if (kind == "image") "dddddddd-dddd-4ddd-8ddd-dddddddddd01" else if (kind == "video") "dddddddd-dddd-4ddd-8ddd-dddddddddd02" else "dddddddd-dddd-4ddd-8ddd-dddddddddd03"
        return """{"messageId":"$id","conversationId":"$conversation","senderId":"$user","senderName":"Аня","body":"$body","createdAt":"2026-09-23T12:00:00Z","kind":"$kind","deleted":$deleted}"""
    }
}
