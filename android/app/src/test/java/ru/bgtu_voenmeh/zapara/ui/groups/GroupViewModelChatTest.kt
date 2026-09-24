package ru.bgtu_voenmeh.zapara.ui.groups

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import kotlinx.coroutines.test.resetMain
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
class GroupViewModelChatTest {
    private val dispatcher = StandardTestDispatcher()
    private val community = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
    private val group = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
    private val direct = "cccccccc-cccc-4ccc-8ccc-cccccccccccc"
    private val user = "dddddddd-dddd-4ddd-8ddd-dddddddddddd"
    private val first = "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeee1"
    private val second = "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeee2"
    private val third = "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeee3"
    private val fourth = "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeee4"
    private val fifth = "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeee5"

    @Before fun setMain() { Dispatchers.setMain(dispatcher) }
    @After fun resetMain() { Dispatchers.resetMain() }

    @Test
    fun editingAnOlderMessageKeepsChronologicalCursorForPolling() = runTest(dispatcher) {
        val http = server()
        val vm = viewModel(http)
        runCurrent()
        try {
            vm.onEvent(GroupEvent.Hold(first, "edit"))
            runCurrent()
            vm.onEvent(GroupEvent.Draft("Исправлено"))
            vm.onEvent(GroupEvent.Send)
            runCurrent()
            assertEquals(listOf(first, second, third), vm.state.value.messages.map { it.id })
            assertEquals("Исправлено", vm.state.value.messages.first().body)

            advanceTimeBy(4000)
            runCurrent()
            assertEquals(listOf(first, second, third, fourth), vm.state.value.messages.map { it.id })
            assertEquals("?after=$third", http.requests.last { it.url.contains("/messages?after=") }.url.substringAfter("/messages"))
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test
    fun switchingChatsLeavesEditModeAndRestoresTheUnsentDraft() = runTest(dispatcher) {
        val vm = viewModel(server())
        runCurrent()
        try {
            vm.onEvent(GroupEvent.Draft("Мой черновик"))
            vm.onEvent(GroupEvent.Hold(first, "edit"))
            runCurrent()
            assertEquals(first, vm.state.value.editing)
            assertEquals("Первое", vm.state.value.draft)

            vm.onEvent(GroupEvent.OpenChat(direct, "Личный чат"))
            runCurrent()
            assertNull(vm.state.value.editing)
            vm.onEvent(GroupEvent.GroupChat)
            runCurrent()
            assertNull(vm.state.value.editing)
            assertEquals("Мой черновик", vm.state.value.draft)
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test
    fun failedDeleteReportsAnErrorWithoutRemovingTheMessage() = runTest(dispatcher) {
        val http = server()
        val normal = http.handler
        http.handler = { call ->
            if (call.url.endsWith("/messages/$first/delete")) {
                HttpReply(403, """{"title":"Нет доступа","status":403,"code":"forbidden"}""".toByteArray())
            } else normal(call)
        }
        val vm = viewModel(http)
        runCurrent()
        try {
            vm.onEvent(GroupEvent.Hold(first, "delete"))
            runCurrent()
            assertTrue("failed=${vm.state.value.failed}, requests=${http.requests.map { it.url }}", vm.state.value.failed)
            assertFalse(vm.state.value.messages.first().deleted)
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test
    fun attachmentChosenDuringAnUploadCannotReplaceTheNextTextSend() = runTest(dispatcher) {
        val http = server()
        val normal = http.handler
        val releaseUpload = CompletableDeferred<Unit>()
        http.handler = { call ->
            when {
                call.url.endsWith("/media") -> {
                    releaseUpload.await()
                    HttpReply(201, message(fourth, "Фото").toByteArray())
                }
                call.method == "POST" && call.url.endsWith("/messages") ->
                    HttpReply(201, message(fourth, "Текст").toByteArray())
                else -> normal(call)
            }
        }
        val vm = viewModel(http)
        runCurrent()
        try {
            vm.onEvent(GroupEvent.Media("image", "one.png", byteArrayOf(1)))
            runCurrent()
            vm.onEvent(GroupEvent.Media("image", "two.png", byteArrayOf(2)))
            runCurrent()
            releaseUpload.complete(Unit)
            runCurrent()

            vm.onEvent(GroupEvent.Draft("Текст"))
            vm.onEvent(GroupEvent.Send)
            runCurrent()
            assertEquals(1, http.requests.count { it.url.endsWith("/media") })
            assertTrue(http.requests.any { it.method == "POST" && it.url.endsWith("/messages") })
        } finally {
            releaseUpload.complete(Unit)
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test
    fun failed_attachment_send_keeps_file_for_retry() = runTest(dispatcher) {
        val http = server()
        val normal = http.handler
        var attempts = 0
        http.handler = { call ->
            if (call.method == "POST" && call.url.endsWith("/media")) {
                attempts++
                if (attempts == 1) HttpReply(503, """{"title":"Недоступно","status":503,"code":"unavailable"}""".toByteArray())
                else HttpReply(201, message(fourth, "Фото").toByteArray())
            } else normal(call)
        }
        val vm = viewModel(http)
        runCurrent()
        try {
            vm.onEvent(GroupEvent.Media("image", "photo.png", byteArrayOf(1, 2, 3)))
            runCurrent()
            assertTrue("attempts=$attempts state=${vm.state.value} requests=${http.requests.map { it.url }}", vm.state.value.failed)
            vm.onEvent(GroupEvent.Send)
            runCurrent()
            assertEquals(2, attempts)
            assertTrue(vm.state.value.messages.any { it.id == fourth })
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test
    fun failed_text_send_preserves_draft_and_retry_adds_message_once() = runTest(dispatcher) {
        val http = server()
        val normal = http.handler
        var attempts = 0
        http.handler = { call ->
            if (call.method == "POST" && call.url.endsWith("/conversations/$group/messages")) {
                attempts++
                if (attempts == 1) HttpReply(503, """{"title":"Недоступно","status":503,"code":"unavailable"}""".toByteArray())
                else HttpReply(201, message(fourth, "Встречаемся после пары").toByteArray())
            } else normal(call)
        }
        val vm = viewModel(http)
        runCurrent()
        try {
            vm.onEvent(GroupEvent.Draft("Встречаемся после пары"))
            vm.onEvent(GroupEvent.Send)
            runCurrent()
            assertTrue(vm.state.value.failed)
            assertEquals("Встречаемся после пары", vm.state.value.draft)
            vm.onEvent(GroupEvent.Send)
            runCurrent()
            assertEquals(2, attempts)
            assertEquals(1, vm.state.value.messages.count { it.id == fourth })
            assertEquals("", vm.state.value.draft)
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test
    fun openingAndPollingTheGroupChatAcknowledgesUnreadMessages() = runTest(dispatcher) {
        val http = server(groupUnread = 2)
        val vm = viewModel(http)
        runCurrent()
        try {
            assertEquals(0, vm.state.value.groupUnread)
            assertEquals(1, http.requests.count { it.url.endsWith("/conversations/$group/read") })

            advanceTimeBy(4000)
            runCurrent()
            assertEquals(2, http.requests.count { it.url.endsWith("/conversations/$group/read") })
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test
    fun opensOnlyMediaFromTheCurrentConversationWithDownloadedBytes() = runTest(dispatcher) {
        val http = mediaServer()
        val opened = mutableListOf<Pair<GroupMessageUi, ByteArray>>()
        val vm = viewModel(http) { item, bytes ->
            opened += item to bytes
            true
        }
        runCurrent()
        try {
            vm.onEvent(GroupEvent.OpenMedia(first))
            runCurrent()
            assertEquals(1, opened.size)
            assertEquals(first, opened.single().first.id)
            assertEquals("image", opened.single().first.kind)
            assertTrue(opened.single().second.contentEquals(byteArrayOf(7, 8, 9)))
            assertFalse(vm.state.value.mediaError)
            assertNull(vm.state.value.mediaLoadingId)
            assertEquals(1, http.requests.count { it.url.endsWith("/messages/$first/media") })
            vm.onEvent(GroupEvent.OpenMedia(second))
            runCurrent()
            assertEquals(1, opened.size)
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test
    fun missingViewerShowsAnErrorWithoutLeavingTheChat() = runTest(dispatcher) {
        val vm = viewModel(mediaServer()) { _, _ -> false }
        runCurrent()
        try {
            vm.onEvent(GroupEvent.OpenMedia(first))
            runCurrent()
            assertTrue(vm.state.value.mediaError)
            assertTrue(vm.state.value.hasHome)
            assertNull(vm.state.value.mediaLoadingId)
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test
    fun switchingChatsWhileMediaDownloadsDoesNotOpenTheOldFile() = runTest(dispatcher) {
        val http = mediaServer()
        val normal = http.handler
        val releaseDownload = CompletableDeferred<Unit>()
        http.handler = { call ->
            if (call.url.endsWith("/messages/$first/media")) releaseDownload.await()
            normal(call)
        }
        var opened = false
        val vm = viewModel(http) { _, _ -> opened = true; true }
        runCurrent()
        try {
            vm.onEvent(GroupEvent.OpenMedia(first))
            runCurrent()
            assertEquals(first, vm.state.value.mediaLoadingId)
            vm.onEvent(GroupEvent.OpenChat(direct, "Личный чат"))
            runCurrent()
            releaseDownload.complete(Unit)
            runCurrent()
            assertFalse(opened)
            assertNull(vm.state.value.mediaLoadingId)
            assertFalse(vm.state.value.mediaError)
        } finally {
            releaseDownload.complete(Unit)
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test
    fun pollingRefreshesRecentEditsAndDeletesWithoutSkippingNewerOrOlderPages() = runTest(dispatcher) {
        val http = server()
        val normal = http.handler
        var latestReads = 0
        http.handler = { call ->
            val path = call.url.substringAfter("/communities")
            when {
                call.method == "GET" && path == "/conversations/$group/messages" -> {
                    latestReads++
                    if (latestReads == 1) jsonReply("""{"messages":[${message(second, "Второе")},${message(third, "Третье")}],"hasMore":true}""")
                    else jsonReply("""{"messages":[${message(second, "Исправлено")},${message(third, "").replace("\"deleted\":false", "\"deleted\":true")},${message(fourth, "Четвёртое")},${message(fifth, "Пятое")}],"hasMore":true}""")
                }
                call.method == "GET" && path == "/conversations/$group/messages?before=$second" ->
                    jsonReply("""{"messages":[${message(first, "Первое")}],"hasMore":false}""")
                call.method == "GET" && path == "/conversations/$group/messages?after=$third" ->
                    jsonReply("""{"messages":[${message(fourth, "Четвёртое")}],"hasMore":true}""")
                else -> normal(call)
            }
        }
        val vm = viewModel(http)
        runCurrent()
        try {
            vm.onEvent(GroupEvent.Older)
            runCurrent()
            assertFalse(vm.state.value.hasMore)
            advanceTimeBy(4000)
            runCurrent()

            assertEquals(listOf(first, second, third, fourth), vm.state.value.messages.map { it.id })
            assertEquals("Первое", vm.state.value.messages[0].body)
            assertEquals("Исправлено", vm.state.value.messages[1].body)
            assertTrue(vm.state.value.messages[2].deleted)
            assertFalse(vm.state.value.hasMore)
            assertEquals(2, http.requests.count { it.url.endsWith("/conversations/$group/read") })
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    private fun viewModel(
        http: FakeHttp,
        opener: suspend (GroupMessageUi, ByteArray) -> Boolean = { _, _ -> false }
    ) = GroupViewModel(
        GroupRuntime(false, user, CommunityHttpClient(http, AccountServerScope.parse("http://127.0.0.1:9/")), { testToken("za_", 4) }, { "O3313" }, opener)
    )

    private fun mediaServer(): FakeHttp {
        val http = server()
        val normal = http.handler
        http.handler = { call ->
            when {
                call.method == "GET" && call.url.endsWith("/conversations/$group/messages") ->
                    jsonReply("""{"messages":[${message(first, "photo.png").replace("\"kind\":\"text\"", "\"kind\":\"image\"")},${message(second, "Второе")}],"hasMore":false}""")
                call.method == "GET" && call.url.endsWith("/messages/$first/media") ->
                    HttpReply(200, byteArrayOf(7, 8, 9), "application/octet-stream")
                else -> normal(call)
            }
        }
        return http
    }

    private fun server(groupUnread: Int = 0) = FakeHttp { call ->
        val path = call.url.substringAfter("/communities")
        when {
            call.method == "GET" && path.isEmpty() -> jsonReply("""[{"communityId":"$community","name":"O3313","description":"Группа","revision":1,"role":"member"}]""")
            path == "/$community/home" -> jsonReply(home(groupUnread))
            path == "/conversations/$group/messages" -> jsonReply(page(first, second, third))
            path == "/conversations/$direct/messages" -> jsonReply(page())
            path == "/conversations/$group/messages?after=$third" -> jsonReply(page(fourth))
            path.startsWith("/conversations/$group/messages?after=") -> jsonReply(page(second, third))
            path.endsWith("/messages/$first/edit") -> jsonReply(message(first, "Исправлено"))
            path.endsWith("/read") -> jsonReply(conversation(if (path.contains(group)) group else direct, if (path.contains(group)) "group" else "direct"))
            else -> HttpReply(404, """{"title":"Нет","status":404,"code":"not_found"}""".toByteArray())
        }
    }

    private fun home(unread: Int) = """{"communityId":"$community","name":"O3313","groupName":"O3313","groupChat":${conversation(group, "group", unread)},"classmates":[{"userId":"$user","username":"student","displayName":"Аня","role":"member","self":true}],"directs":[${conversation(direct, "direct")}] }"""
    private fun conversation(id: String, kind: String, unread: Int = 0) = """{"conversationId":"$id","kind":"$kind","communityId":"$community","title":"O3313","peerUserId":${if (kind == "group") "null" else "\"$user\""},"lastBody":null,"lastAt":null,"unread":$unread}"""
    private fun page(vararg ids: String) = """{"messages":[${ids.joinToString(",") { message(it, when (it) { first -> "Первое"; second -> "Второе"; third -> "Третье"; else -> "Четвёртое" }) }}],"hasMore":false}"""
    private fun message(id: String, body: String) = """{"messageId":"$id","conversationId":"$group","senderId":"$user","senderName":"Аня","body":"$body","createdAt":"2026-09-23T12:00:00Z","kind":"text","deleted":false}"""
}
