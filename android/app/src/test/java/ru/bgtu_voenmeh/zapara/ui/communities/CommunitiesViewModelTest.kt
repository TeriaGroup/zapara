package ru.bgtu_voenmeh.zapara.ui.communities

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.resetMain
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
import ru.bgtu_voenmeh.zapara.data.api.FakeHttp
import ru.bgtu_voenmeh.zapara.data.api.HttpCall
import ru.bgtu_voenmeh.zapara.data.api.HttpReply
import ru.bgtu_voenmeh.zapara.data.communities.CommunityHttpClient

@OptIn(ExperimentalCoroutinesApi::class)
class CommunitiesViewModelTest {
    private val dispatcher = StandardTestDispatcher()

    @Before fun setMain() { Dispatchers.setMain(dispatcher) }
    @After fun reset() { Dispatchers.resetMain() }

    @Test
    fun guest_needs_account_and_does_not_call_the_client() = runTest(dispatcher) {
        val http = FakeHttp { error("guest must not call the network") }
        val vm = CommunitiesViewModel(
            CommunitiesRuntime(
                guest = true,
                client = CommunityHttpClient(http, SCOPE),
                accessToken = { ACCESS },
                groupId = { "O3313" }
            )
        )
        advanceUntilIdle()
        assertEquals(CommunityPane.Guest, vm.state.value.pane)
        assertTrue(vm.state.value.communities.isEmpty())
        assertTrue(http.requests.isEmpty())
    }

    @Test
    fun signed_in_without_client_is_need_account() = runTest(dispatcher) {
        val vm = CommunitiesViewModel(
            CommunitiesRuntime(guest = false, client = null, accessToken = { ACCESS }, groupId = { null })
        )
        advanceUntilIdle()
        assertEquals(CommunityPane.Guest, vm.state.value.pane)
    }

    @Test
    fun signed_in_empty_catalog_is_empty_pane() = runTest(dispatcher) {
        val http = FakeHttp { ok("[]") }
        val vm = signedIn(http)
        advanceUntilIdle()
        assertEquals(CommunityPane.Empty, vm.state.value.pane)
        assertTrue(vm.state.value.communities.isEmpty())
    }

    @Test
    fun forbidden_hides_catalog() = runTest(dispatcher) {
        val http = FakeHttp { problem(403, "forbidden") }
        val vm = signedIn(http)
        advanceUntilIdle()
        assertEquals(CommunityPane.Forbidden, vm.state.value.pane)
        assertTrue(vm.state.value.communities.isEmpty())
        assertNull(vm.state.value.selected)
    }

    @Test
    fun catalog_join_marks_pending() = runTest(dispatcher) {
        val http = FakeHttp { call ->
            when (suffix(call)) {
                "GET " -> ok(arr(communityJson(null)))
                "POST /$CID/join-requests" -> created(joinJson("pending"))
                else -> error(suffix(call))
            }
        }
        val vm = signedIn(http, groupId = null)
        advanceUntilIdle()
        assertEquals(CommunityPane.Catalog, vm.state.value.pane)
        val row = vm.state.value.communities.single()
        assertTrue(row.canJoin)
        assertFalse(row.canOpen)
        vm.onEvent(CommunitiesEvent.Join(CID))
        advanceUntilIdle()
        val pending = vm.state.value.communities.single()
        assertEquals("pending", pending.joinStatus)
        assertFalse(pending.canJoin)
    }

    @Test
    fun member_open_loads_personal_homework() = runTest(dispatcher) {
        val http = FakeHttp { call ->
            when (suffix(call)) {
                "GET " -> ok(arr(communityJson("member")))
                "GET /$CID/homework" -> ok(arr(homeworkJson(HID, "Задание А")))
                "GET /$CID/homework/$HID/completion" -> ok(completionJson(HID, true, 1))
                "GET /$CID/announcements" -> ok("[]")
                "GET /$CID/polls" -> ok("[]")
                else -> error(suffix(call))
            }
        }
        val vm = signedIn(http, groupId = null)
        advanceUntilIdle()
        assertEquals(CommunityPane.Catalog, vm.state.value.pane)
        assertTrue(vm.state.value.communities.single().canOpen)
        vm.onEvent(CommunitiesEvent.Open(CID))
        advanceUntilIdle()
        assertEquals(CommunityPane.Detail, vm.state.value.pane)
        val homework = vm.state.value.selected!!.homework.single()
        assertEquals("Задание А", homework.title)
        assertTrue(homework.completed)
        assertTrue(homework.canToggle)
        assertTrue(http.requests.any { it.url.endsWith("/homework/$HID/completion") && it.method == "GET" })
        assertTrue(http.requests.none { it.url.contains("/join-requests") && it.method == "GET" })
    }

    private fun signedIn(http: FakeHttp, groupId: String? = "O3313") = CommunitiesViewModel(
        CommunitiesRuntime(
            guest = false,
            client = CommunityHttpClient(http, SCOPE),
            accessToken = { ACCESS },
            groupId = { groupId }
        )
    )
}

private val SCOPE = AccountServerScope.parse("https://example.invalid/root")
private val ACCESS: String = run {
    val encoded = java.util.Base64.getUrlEncoder().withoutPadding().encodeToString(ByteArray(32) { 1 })
    "za_$encoded"
}
private const val BASE = "https://example.invalid/root/api/v1/communities"
private const val CID = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
private const val HID = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
private const val RID = "11111111-1111-4111-8111-111111111111"
private const val UID = "dddddddd-dddd-4ddd-8ddd-dddddddddddd"
private const val AT = "2026-09-08T12:00:00Z"

private fun suffix(call: HttpCall) = call.method + " " + call.url.removePrefix(BASE)
private fun ok(body: String) = HttpReply(200, body.toByteArray())
private fun created(body: String) = HttpReply(201, body.toByteArray())
private fun problem(status: Int, code: String) =
    HttpReply(status, """{"title":"Ошибка","status":$status,"code":"$code"}""".toByteArray())
private fun arr(vararg items: String) = items.joinToString(",", "[", "]")
private fun communityJson(role: String?) =
    """{"communityId":"$CID","name":"Группа О3313","description":"","revision":1,"role":${if (role == null) "null" else "\"$role\""}}"""
private fun joinJson(status: String) =
    """{"requestId":"$RID","communityId":"$CID","userId":"$UID","status":"$status","createdAt":"$AT"}"""
private fun homeworkJson(id: String, title: String) =
    """{"homeworkId":"$id","communityId":"$CID","title":"$title","body":"Текст","revision":1,"createdAt":"$AT","updatedAt":"$AT"}"""
private fun completionJson(id: String, completed: Boolean, revision: Long) =
    """{"homeworkId":"$id","completed":$completed,"revision":$revision,"updatedAt":${if (completed) "\"$AT\"" else "null"}}"""
