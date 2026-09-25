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
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.accounts.testToken
import ru.bgtu_voenmeh.zapara.data.api.FakeHttp
import ru.bgtu_voenmeh.zapara.data.api.HttpReply
import ru.bgtu_voenmeh.zapara.data.api.jsonReply
import ru.bgtu_voenmeh.zapara.data.communities.CommunityHttpClient
import ru.bgtu_voenmeh.zapara.data.communities.GroupDesk
import ru.bgtu_voenmeh.zapara.data.communities.GroupPower
import ru.bgtu_voenmeh.zapara.data.communities.GroupRole

@OptIn(ExperimentalCoroutinesApi::class)
class GroupViewModelChannelsTest {
    private val dispatcher = StandardTestDispatcher()
    private val community = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
    private val conversation = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
    private val direct = "66666666-6666-4666-8666-666666666666"
    private val firstMessage = "77777777-7777-4777-8777-777777777777"
    private val chatTopic = "cccccccc-cccc-4ccc-8ccc-cccccccccccc"
    private val ballotTopic = "dddddddd-dddd-4ddd-8ddd-dddddddddddd"
    private val newTopic = "99999999-9999-4999-8999-999999999999"
    private val trustedRole = "88888888-8888-4888-8888-888888888888"
    private val ballot = "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee"
    private val option = "ffffffff-ffff-4fff-8fff-ffffffffffff"
    private val user = "11111111-1111-4111-8111-111111111111"

    @Before fun setMain() { Dispatchers.setMain(dispatcher) }
    @After fun resetMain() { Dispatchers.resetMain() }

    @Test fun group_entry_opens_channel_list_without_marking_any_chat_read() = runTest(dispatcher) {
        val http = server()
        val vm = GroupViewModel(GroupRuntime(false, user,
            CommunityHttpClient(http, AccountServerScope.parse("http://127.0.0.1:9/")),
            { testToken("za_", 4) }, { "O3313" }, startInChannelList = true))
        runCurrent()
        try {
            assertTrue(vm.state.value.showChannels)
            assertEquals(3, vm.state.value.channels.size)
            assertFalse(http.requests.any { it.url.contains("/conversations/$conversation/messages") })
            assertFalse(http.requests.any { it.url.endsWith("/conversations/$conversation/read") })
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test fun selecting_chat_topic_scopes_messages_and_keeps_each_draft() = runTest(dispatcher) {
        val http = server()
        val vm = viewModel(http)
        runCurrent()
        assertTrue(http.requests.any { it.url.endsWith("/conversations/$conversation/messages?topic=general") })
        vm.onEvent(GroupEvent.Draft("Общий черновик"))
        vm.onEvent(GroupEvent.OpenChannel(chatTopic))
        runCurrent()
        assertEquals(chatTopic, vm.state.value.activeTopicId)
        assertTrue(http.requests.any { it.url.endsWith("/conversations/$conversation/messages?topic=$chatTopic") })
        vm.onEvent(GroupEvent.Draft("Тема"))
        vm.onEvent(GroupEvent.Send)
        runCurrent()
        assertTrue(http.requests.any { it.url.endsWith("/conversations/$conversation/topic-messages") && String(it.body!!).contains("\"topicId\":\"$chatTopic\"") })
        vm.onEvent(GroupEvent.OpenChannel(null))
        runCurrent()
        assertEquals("Общий черновик", vm.state.value.draft)
        vm.onEvent(GroupEvent.Back)
        runCurrent()
    }

    @Test fun ballot_channel_loads_only_its_board_and_never_sends_chat_text() = runTest(dispatcher) {
        val http = server()
        val vm = viewModel(http)
        runCurrent()
        vm.onEvent(GroupEvent.OpenChannel(ballotTopic))
        runCurrent()
        assertEquals("ballots", vm.state.value.activeChannelKind)
        assertEquals(ballot, vm.state.value.board?.ballots?.single()?.ballotId)
        assertTrue(http.requests.any { it.url.endsWith("/$community/ballots?topic=$ballotTopic") })
        vm.onEvent(GroupEvent.Draft("Это не сообщение"))
        vm.onEvent(GroupEvent.Send)
        runCurrent()
        assertFalse(http.requests.any { it.method == "POST" && it.url.endsWith("/topic-messages") })
        vm.onEvent(GroupEvent.VoteBallot(ballot, option))
        runCurrent()
        assertTrue(vm.state.value.board!!.ballots.single().options.single().chosen)
        assertEquals(2, http.requests.count { it.url.endsWith("/$community/ballots?topic=$ballotTopic") })
        vm.onEvent(GroupEvent.Back)
        runCurrent()
    }

    @Test fun successful_vote_remains_visible_when_scoped_refresh_fails() = runTest(dispatcher) {
        val http = server()
        val normal = http.handler
        var voted = false
        http.handler = { call -> when {
            call.method == "POST" && call.url.endsWith("/$community/ballots/$ballot/votes") -> {
                voted = true
                jsonReply(board(true))
            }
            voted && call.method == "GET" && call.url.endsWith("/$community/ballots?topic=$ballotTopic") ->
                HttpReply(503, """{"title":"Недоступно","status":503,"code":"db_unavailable"}""".toByteArray())
            else -> normal(call)
        } }
        val vm = viewModel(http)
        runCurrent()
        try {
            vm.onEvent(GroupEvent.OpenChannel(ballotTopic))
            runCurrent()
            assertFalse(vm.state.value.board!!.ballots.single().options.single().chosen)
            vm.onEvent(GroupEvent.VoteBallot(ballot, option))
            runCurrent()
            assertTrue(vm.state.value.board!!.ballots.single().options.single().chosen)
            assertTrue(vm.state.value.ballotRefreshFailed)
            assertFalse(vm.state.value.failed)
            assertEquals(1, http.requests.count { it.method == "POST" && it.url.endsWith("/$community/ballots/$ballot/votes") })
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test fun expired_session_does_not_leave_ballot_channel_loading_forever() = runTest(dispatcher) {
        var expired = false
        val vm = GroupViewModel(GroupRuntime(false, user,
            CommunityHttpClient(server(), AccountServerScope.parse("http://127.0.0.1:9/")),
            { if (expired) null else testToken("za_", 4) }, { "O3313" }))
        runCurrent()
        try {
            expired = true
            vm.onEvent(GroupEvent.OpenChannel(ballotTopic))
            runCurrent()
            assertFalse(vm.state.value.chatLoading)
            assertTrue(vm.state.value.failed)
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test fun ordinary_member_cannot_create_a_channel_from_client_event() = runTest(dispatcher) {
        val http = server()
        val vm = viewModel(http)
        runCurrent()
        assertFalse(vm.state.value.canManageChannels)
        vm.onEvent(GroupEvent.CreateChannel("Секрет", "🔒", "chat"))
        runCurrent()
        assertFalse(http.requests.any { it.method == "POST" && it.url.endsWith("/$community/topics?typed=1") })
        vm.onEvent(GroupEvent.Back)
        runCurrent()
    }

    @Test fun channel_list_keeps_unsent_draft_without_an_active_composer() = runTest(dispatcher) {
        val vm = viewModel(server())
        runCurrent()
        try {
            vm.onEvent(GroupEvent.Draft("Напомнить группе"))
            vm.onEvent(GroupEvent.Channels)
            runCurrent()
            assertTrue(vm.state.value.showChannels)
            assertEquals(null, vm.state.value.activeConversationId)
            vm.onEvent(GroupEvent.OpenChannel(null))
            runCurrent()
            assertEquals("Напомнить группе", vm.state.value.draft)
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test fun trusted_manager_can_create_a_typed_channel() = runTest(dispatcher) {
        val http = server(canManage = true)
        val vm = viewModel(http)
        runCurrent()
        assertTrue(vm.state.value.canManageChannels)
        vm.onEvent(GroupEvent.CreateChannel("Консультации", "📚", "chat"))
        runCurrent()
        assertTrue(http.requests.any { it.method == "POST" && it.url.endsWith("/$community/topics?typed=1") &&
            String(it.body!!) == """{"title":"Консультации","icon":"📚","kind":"chat","description":"","accent":"default","pinned":false,"writePolicy":"all"}""" })
        assertTrue(vm.state.value.channels.any { it.topicId == newTopic && it.title == "Консультации" })
        vm.onEvent(GroupEvent.Back)
        runCurrent()
    }

    @Test fun a_send_waiting_for_session_does_not_move_into_another_channel() = runTest(dispatcher) {
        val http = server()
        val delayed = CompletableDeferred<String>()
        var holdNext = false
        val vm = GroupViewModel(GroupRuntime(false, user,
            CommunityHttpClient(http, AccountServerScope.parse("http://127.0.0.1:9/")),
            { if (holdNext) { holdNext = false; delayed.await() } else testToken("za_", 4) }, { "O3313" }))
        runCurrent()
        try {
            vm.onEvent(GroupEvent.Draft("Черновик общего"))
            holdNext = true
            vm.onEvent(GroupEvent.Send)
            runCurrent()
            vm.onEvent(GroupEvent.OpenChannel(chatTopic))
            runCurrent()
            delayed.complete(testToken("za_", 4))
            runCurrent()
            assertFalse(http.requests.any { it.method == "POST" && it.url.endsWith("/topic-messages") })
            vm.onEvent(GroupEvent.OpenChannel(null))
            runCurrent()
            assertEquals("Черновик общего", vm.state.value.draft)
        } finally {
            delayed.complete(testToken("za_", 4))
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test fun slow_send_in_one_topic_does_not_block_another_topic_composer() = runTest(dispatcher) {
        val http = server()
        val normal = http.handler
        val releaseFirst = CompletableDeferred<Unit>()
        http.handler = { call ->
            if (call.method == "POST" && call.url.endsWith("/conversations/$conversation/topic-messages")) {
                val body = String(call.body!!)
                if (body.contains("\"topicId\":null")) {
                    releaseFirst.await()
                    HttpReply(201, """{"messageId":"$ballot","conversationId":"$conversation","senderId":"$user","senderName":"Аня","body":"Первое","createdAt":"2026-09-25T12:00:00Z"}""".toByteArray())
                } else HttpReply(201, """{"messageId":"$newTopic","conversationId":"$conversation","senderId":"$user","senderName":"Аня","body":"Второе","createdAt":"2026-09-25T12:00:01Z"}""".toByteArray())
            } else normal(call)
        }
        val vm = viewModel(http)
        runCurrent()
        try {
            vm.onEvent(GroupEvent.Draft("Первое"))
            vm.onEvent(GroupEvent.Send)
            runCurrent()
            vm.onEvent(GroupEvent.OpenChannel(chatTopic))
            runCurrent()
            assertFalse(vm.state.value.sending)
            vm.onEvent(GroupEvent.Draft("Второе"))
            vm.onEvent(GroupEvent.Send)
            runCurrent()
            assertEquals(2, http.requests.count { it.method == "POST" && it.url.endsWith("/topic-messages") })
            assertEquals("Второе", vm.state.value.messages.last().body)
            releaseFirst.complete(Unit)
            runCurrent()
            assertFalse(vm.state.value.sending)
        } finally {
            releaseFirst.complete(Unit)
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test fun failed_send_restores_visible_draft_after_returning_to_its_channel() = runTest(dispatcher) {
        val http = server()
        val normal = http.handler
        val release = CompletableDeferred<Unit>()
        http.handler = { call ->
            if (call.method == "POST" && call.url.endsWith("/conversations/$conversation/topic-messages")) {
                release.await()
                HttpReply(503, """{"title":"Недоступно","status":503,"code":"db_unavailable"}""".toByteArray())
            } else normal(call)
        }
        val vm = viewModel(http)
        runCurrent()
        try {
            vm.onEvent(GroupEvent.Draft("Первое"))
            vm.onEvent(GroupEvent.Send)
            runCurrent()
            vm.onEvent(GroupEvent.OpenChannel(chatTopic))
            runCurrent()
            vm.onEvent(GroupEvent.OpenChannel(null))
            runCurrent()
            release.complete(Unit)
            runCurrent()
            assertEquals("Первое", vm.state.value.draft)
            assertFalse(vm.state.value.sending)
            assertTrue(vm.state.value.failed)
        } finally {
            release.complete(Unit)
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test fun successful_reply_clears_reopened_context_and_restores_plain_draft() = runTest(dispatcher) {
        val http = server()
        val normal = http.handler
        val release = CompletableDeferred<Unit>()
        http.handler = { call -> when {
            call.method == "GET" && call.url.endsWith("/conversations/$conversation/messages?topic=general") ->
                jsonReply("""{"messages":[{"messageId":"$firstMessage","conversationId":"$conversation","senderId":"$user","senderName":"Аня","body":"Старое","createdAt":"2026-09-25T12:00:00Z","kind":"text","deleted":false}],"hasMore":false}""")
            call.method == "POST" && call.url.endsWith("/conversations/$conversation/topic-messages") -> {
                release.await()
                HttpReply(201, """{"messageId":"$newTopic","conversationId":"$conversation","senderId":"$user","senderName":"Аня","body":"Ответ","createdAt":"2026-09-25T12:00:01Z","replyTo":"$firstMessage"}""".toByteArray())
            }
            else -> normal(call)
        } }
        val vm = viewModel(http)
        runCurrent()
        try {
            vm.onEvent(GroupEvent.Draft("Мой черновик"))
            vm.onEvent(GroupEvent.Hold(firstMessage, "reply"))
            runCurrent()
            vm.onEvent(GroupEvent.Draft("Ответ"))
            vm.onEvent(GroupEvent.Send)
            runCurrent()
            vm.onEvent(GroupEvent.OpenChannel(chatTopic))
            runCurrent()
            vm.onEvent(GroupEvent.OpenChannel(null))
            runCurrent()
            assertEquals(firstMessage, vm.state.value.replyTo)
            release.complete(Unit)
            runCurrent()
            assertEquals(null, vm.state.value.replyTo)
            assertEquals(null, vm.state.value.editing)
            assertEquals("Мой черновик", vm.state.value.draft)
            assertTrue(vm.state.value.messages.any { it.id == newTopic })
        } finally {
            release.complete(Unit)
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test fun late_send_result_does_not_overwrite_a_newer_draft_in_reopened_channel() = runTest(dispatcher) {
        for (status in listOf(201, 503)) {
            val http = server()
            val normal = http.handler
            val release = CompletableDeferred<Unit>()
            http.handler = { call ->
                if (call.method == "POST" && call.url.endsWith("/conversations/$conversation/topic-messages")) {
                    release.await()
                    if (status == 201) HttpReply(201,
                        """{"messageId":"$newTopic","conversationId":"$conversation","senderId":"$user","senderName":"Аня","body":"Первое","createdAt":"2026-09-25T12:00:01Z"}""".toByteArray())
                    else HttpReply(503, """{"title":"Недоступно","status":503,"code":"db_unavailable"}""".toByteArray())
                } else normal(call)
            }
            val vm = viewModel(http)
            runCurrent()
            try {
                vm.onEvent(GroupEvent.Draft("Первое"))
                vm.onEvent(GroupEvent.Send)
                runCurrent()
                vm.onEvent(GroupEvent.OpenChannel(chatTopic))
                runCurrent()
                vm.onEvent(GroupEvent.OpenChannel(null))
                runCurrent()
                vm.onEvent(GroupEvent.Draft("Позднее"))
                release.complete(Unit)
                runCurrent()
                assertEquals("status=$status", "Позднее", vm.state.value.draft)
            } finally {
                release.complete(Unit)
                vm.onEvent(GroupEvent.Back)
                runCurrent()
            }
        }
    }

    @Test fun reply_and_edit_context_survive_switching_topics() = runTest(dispatcher) {
        val http = server()
        val normal = http.handler
        http.handler = { call ->
            if (call.method == "GET" && call.url.endsWith("/conversations/$conversation/messages?topic=general"))
                jsonReply("""{"messages":[{"messageId":"$firstMessage","conversationId":"$conversation","senderId":"$user","senderName":"Аня","body":"Старое","createdAt":"2026-09-25T12:00:00Z","kind":"text","deleted":false}],"hasMore":false}""")
            else normal(call)
        }
        val vm = viewModel(http)
        runCurrent()
        try {
            vm.onEvent(GroupEvent.Hold(firstMessage, "reply"))
            runCurrent()
            vm.onEvent(GroupEvent.Draft("Ответ"))
            vm.onEvent(GroupEvent.OpenChannel(chatTopic))
            runCurrent()
            vm.onEvent(GroupEvent.OpenChannel(null))
            runCurrent()
            assertEquals(firstMessage, vm.state.value.replyTo)
            assertEquals("Ответ", vm.state.value.draft)

            vm.onEvent(GroupEvent.Hold(firstMessage, "edit"))
            runCurrent()
            vm.onEvent(GroupEvent.Draft("Правка"))
            vm.onEvent(GroupEvent.OpenChannel(chatTopic))
            runCurrent()
            vm.onEvent(GroupEvent.OpenChannel(null))
            runCurrent()
            assertEquals(firstMessage, vm.state.value.editing)
            assertEquals("Правка", vm.state.value.draft)
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test fun headman_can_create_a_trusted_role_and_grant_it_to_a_classmate() = runTest(dispatcher) {
        val http = server(canManage = true, headman = true)
        val vm = viewModel(http)
        runCurrent()
        try {
            vm.onEvent(GroupEvent.Trusted)
            runCurrent()
            assertTrue(vm.state.value.desk!!.headman)
            vm.onEvent(GroupEvent.CreateTrustedRole("Доверенные"))
            runCurrent()
            assertTrue(vm.state.value.desk!!.powers.any { it.roleId == trustedRole && it.power == "channels" })
            vm.onEvent(GroupEvent.GrantTrusted(trustedRole, newTopic))
            runCurrent()
            assertTrue(vm.state.value.desk!!.grants.any { it.roleId == trustedRole && it.userId == newTopic })
            assertTrue(http.requests.any { it.url.endsWith("/$community/roles/$trustedRole/grants") && it.method == "POST" })
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test fun headman_can_enable_channels_on_an_existing_custom_role() = runTest(dispatcher) {
        val http = server(canManage = true, headman = true, existingRole = true)
        val vm = viewModel(http)
        runCurrent()
        try {
            vm.onEvent(GroupEvent.Trusted)
            runCurrent()
            assertTrue(vm.state.value.desk!!.roles.any { it.roleId == trustedRole })
            assertFalse(vm.state.value.desk!!.powers.any { it.power == "channels" })
            vm.onEvent(GroupEvent.EnableTrustedRole(trustedRole))
            runCurrent()
            assertTrue(vm.state.value.desk!!.powers.any { it.roleId == trustedRole && it.power == "channels" })
            assertFalse(http.requests.any { it.method == "POST" && it.url.endsWith("/$community/roles") })
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test fun closing_trusted_panel_ignores_a_late_desk_response() = runTest(dispatcher) {
        val http = server(canManage = true, headman = true)
        val normal = http.handler
        val releaseDesk = CompletableDeferred<Unit>()
        http.handler = { call ->
            if (call.method == "GET" && call.url.endsWith("/$community/desk")) {
                releaseDesk.await()
                jsonReply(desk(true, false, false, false))
            } else normal(call)
        }
        val vm = viewModel(http)
        runCurrent()
        try {
            vm.onEvent(GroupEvent.Trusted)
            runCurrent()
            assertTrue(vm.state.value.showTrusted)
            vm.onEvent(GroupEvent.CloseTrusted)
            runCurrent()
            releaseDesk.complete(Unit)
            runCurrent()
            assertFalse(vm.state.value.showTrusted)
            assertEquals(null, vm.state.value.desk)
        } finally {
            releaseDesk.complete(Unit)
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test fun expired_session_settles_trusted_panel_loading() = runTest(dispatcher) {
        var expired = false
        val vm = GroupViewModel(GroupRuntime(false, user,
            CommunityHttpClient(server(canManage = true, headman = true), AccountServerScope.parse("http://127.0.0.1:9/")),
            { if (expired) null else testToken("za_", 4) }, { "O3313" }))
        runCurrent()
        try {
            expired = true
            vm.onEvent(GroupEvent.Trusted)
            runCurrent()
            assertFalse(vm.state.value.channelBusy)
            assertTrue(vm.state.value.failed)
            assertFalse(vm.state.value.showTrusted)
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test fun trusted_chooser_excludes_roles_with_other_powers() = runTest(dispatcher) {
        val desk = GroupDesk(true,
            listOf(GroupRole(trustedRole, "Широкая роль"), GroupRole(chatTopic, "Только каналы")), emptyList(),
            listOf(GroupPower(trustedRole, "channels"), GroupPower(trustedRole, "roles"), GroupPower(chatTopic, "channels")), emptyList())
        assertEquals(listOf(chatTopic), trustedChannelRoles(desk).map { it.roleId })

        val http = server(canManage = true, headman = true, existingRole = true)
        val normal = http.handler
        http.handler = { call ->
            if (call.method == "GET" && call.url.endsWith("/$community/desk"))
                jsonReply("""{"headman":true,"roles":[{"roleId":"$trustedRole","name":"Широкая роль"}],"grants":[],"applicants":[],"powers":[{"roleId":"$trustedRole","power":"channels"},{"roleId":"$trustedRole","power":"roles"}],"mine":[]}""")
            else normal(call)
        }
        val vm = viewModel(http)
        runCurrent()
        try {
            vm.onEvent(GroupEvent.Trusted)
            runCurrent()
            vm.onEvent(GroupEvent.GrantTrusted(trustedRole, newTopic))
            runCurrent()
            assertFalse(http.requests.any { it.method == "POST" && it.url.endsWith("/$community/roles/$trustedRole/grants") })
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test fun restricted_chat_is_readable_but_does_not_send_member_draft() = runTest(dispatcher) {
        val http = server(restrictedChat = true, pinnedBallot = true)
        val vm = viewModel(http)
        runCurrent()
        try {
            assertEquals(listOf(null, ballotTopic, chatTopic), vm.state.value.channels.map { it.topicId })
            vm.onEvent(GroupEvent.OpenChannel(chatTopic))
            runCurrent()
            assertFalse(vm.state.value.canPost)
            assertTrue(http.requests.any { it.url.endsWith("/conversations/$conversation/messages?topic=$chatTopic") })
            vm.onEvent(GroupEvent.Draft("Нельзя отправить"))
            vm.onEvent(GroupEvent.Send)
            runCurrent()
            assertFalse(http.requests.any { it.method == "POST" && it.url.endsWith("/topic-messages") })
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test fun restricted_ballot_channel_still_accepts_votes_but_not_new_ballots() = runTest(dispatcher) {
        val http = server(restrictedBallot = true)
        val vm = viewModel(http)
        runCurrent()
        try {
            vm.onEvent(GroupEvent.OpenChannel(ballotTopic))
            runCurrent()
            assertFalse(vm.state.value.canPost)
            vm.onEvent(GroupEvent.CreateBallot("Когда?", listOf("Да", "Нет"), 3, false))
            runCurrent()
            assertFalse(http.requests.any { it.url.endsWith("/$community/ballots/collective") })
            vm.onEvent(GroupEvent.VoteBallot(ballot, option))
            runCurrent()
            assertTrue(vm.state.value.board!!.ballots.single().options.single().chosen)
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test fun global_ballot_board_is_available_after_a_ballot_channel_is_removed() = runTest(dispatcher) {
        val http = server()
        val vm = viewModel(http)
        runCurrent()
        try {
            vm.onEvent(GroupEvent.GlobalBallots("Все голосования"))
            runCurrent()
            assertEquals("ballots", vm.state.value.activeChannelKind)
            assertEquals(null, vm.state.value.activeTopicId)
            assertTrue(http.requests.any { it.url.endsWith("/$community/ballots") && it.method == "GET" })
            assertEquals(ballot, vm.state.value.board!!.ballots.single().ballotId)
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test fun ballot_channel_refreshes_revoked_write_permission() = runTest(dispatcher) {
        val http = server()
        val normal = http.handler
        var restricted = false
        http.handler = { call ->
            if (call.method == "GET" && call.url.endsWith("/$community/topics?typed=1"))
                jsonReply(topicList(false, restrictedBallot = restricted))
            else normal(call)
        }
        val vm = viewModel(http)
        runCurrent()
        try {
            vm.onEvent(GroupEvent.OpenChannel(ballotTopic))
            runCurrent()
            assertTrue(vm.state.value.canPost)
            restricted = true
            advanceTimeBy(8000)
            runCurrent()
            assertFalse(vm.state.value.canPost)
            vm.onEvent(GroupEvent.CreateBallot("Когда?", listOf("Да", "Нет"), 3, false))
            runCurrent()
            assertFalse(http.requests.any { it.url.endsWith("/$community/ballots/collective") })
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test fun channel_list_refreshes_revoked_management_permission() = runTest(dispatcher) {
        val http = server(canManage = true)
        val normal = http.handler
        var allowed = true
        http.handler = { call ->
            if (call.method == "GET" && call.url.endsWith("/$community/topics?typed=1"))
                jsonReply(topicList(allowed))
            else normal(call)
        }
        val vm = GroupViewModel(GroupRuntime(false, user,
            CommunityHttpClient(http, AccountServerScope.parse("http://127.0.0.1:9/")),
            { testToken("za_", 4) }, { "O3313" }, startInChannelList = true))
        runCurrent()
        try {
            assertTrue(vm.state.value.canManageChannels)
            allowed = false
            advanceTimeBy(8000)
            runCurrent()
            assertFalse(vm.state.value.canManageChannels)
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test fun forbidden_channel_change_removes_stale_manage_controls_immediately() = runTest(dispatcher) {
        val http = server(canManage = true)
        val normal = http.handler
        var allowed = true
        http.handler = { call -> when {
            call.method == "GET" && call.url.endsWith("/$community/topics?typed=1") -> jsonReply(topicList(allowed))
            call.method == "POST" && call.url.endsWith("/$community/topics?typed=1") -> {
                allowed = false
                HttpReply(403, """{"title":"Нет доступа","status":403,"code":"forbidden"}""".toByteArray())
            }
            else -> normal(call)
        } }
        val vm = viewModel(http)
        runCurrent()
        try {
            assertTrue(vm.state.value.canManageChannels)
            vm.onEvent(GroupEvent.CreateChannel("Канал", "💬", "chat"))
            runCurrent()
            assertFalse(vm.state.value.canManageChannels)
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test fun older_server_rejecting_typed_topics_keeps_legacy_group_chat_available() = runTest(dispatcher) {
        val http = server()
        val normal = http.handler
        http.handler = { call -> when {
            call.method == "GET" && call.url.endsWith("/$community/topics?typed=1") ->
                HttpReply(400, """{"title":"Bad query","status":400,"code":"invalid_request"}""".toByteArray())
            call.method == "GET" && call.url.endsWith("/conversations/$conversation/messages") ->
                jsonReply("""{"messages":[],"hasMore":false}""")
            else -> normal(call)
        } }
        val vm = viewModel(http)
        runCurrent()
        try {
            assertTrue(vm.state.value.hasHome)
            assertFalse(vm.state.value.failed)
            assertEquals(1, vm.state.value.channels.size)
            assertEquals(conversation, vm.state.value.activeConversationId)
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    @Test fun channel_routing_does_not_block_direct_chat_media() = runTest(dispatcher) {
        val http = server()
        val vm = viewModel(http)
        runCurrent()
        try {
            vm.onEvent(GroupEvent.OpenChat(direct, "Личный чат"))
            runCurrent()
            assertEquals("direct", vm.state.value.activeChannelKind)
            vm.onEvent(GroupEvent.Media("image", "photo.png", byteArrayOf(1), direct))
            runCurrent()
            assertTrue(http.requests.any { it.method == "POST" && it.url.endsWith("/conversations/$direct/media") })
        } finally {
            vm.onEvent(GroupEvent.Back)
            runCurrent()
        }
    }

    private fun viewModel(http: FakeHttp) = GroupViewModel(GroupRuntime(false, user,
        CommunityHttpClient(http, AccountServerScope.parse("http://127.0.0.1:9/")),
        { testToken("za_", 4) }, { "O3313" }))

    private fun server(canManage: Boolean = false, headman: Boolean = false,
        restrictedChat: Boolean = false, pinnedBallot: Boolean = false, restrictedBallot: Boolean = false,
        existingRole: Boolean = false): FakeHttp {
        var chosen = false
        var roleCreated = existingRole
        var powerEnabled = false
        var granted = false
        return FakeHttp { call ->
            val path = call.url.substringAfter("/communities")
            when {
                call.method == "GET" && path.isEmpty() -> jsonReply("""[{"communityId":"$community","name":"O3313","description":"Группа","revision":1,"role":"member"}]""")
                call.method == "GET" && path == "/$community/home" -> jsonReply("""{"communityId":"$community","name":"O3313","groupName":"O3313","groupChat":{"conversationId":"$conversation","kind":"group","communityId":"$community","title":"O3313","peerUserId":null,"lastBody":null,"lastAt":null,"unread":0},"classmates":[{"userId":"$user","username":"student","displayName":"Аня","role":"${if (headman) "headman" else "member"}","self":true},{"userId":"$newTopic","username":"other","displayName":"Друг","role":"member","self":false}],"directs":[]}""")
                call.method == "GET" && path == "/$community/topics?typed=1" -> jsonReply(topicList(canManage, false, restrictedChat, pinnedBallot, restrictedBallot))
                call.method == "POST" && path == "/$community/topics?typed=1" -> HttpReply(201, topicList(canManage, true, restrictedChat, pinnedBallot, restrictedBallot).toByteArray())
                call.method == "GET" && path == "/$community/desk" -> jsonReply(desk(headman, roleCreated, powerEnabled, granted))
                call.method == "POST" && path == "/$community/roles" -> { roleCreated = true; HttpReply(201, desk(headman, roleCreated, powerEnabled, granted).toByteArray()) }
                call.method == "POST" && path == "/$community/roles/$trustedRole/powers" -> { powerEnabled = true; jsonReply(desk(headman, roleCreated, powerEnabled, granted)) }
                call.method == "POST" && path == "/$community/roles/$trustedRole/grants" -> { granted = true; jsonReply(desk(headman, roleCreated, powerEnabled, granted)) }
                call.method == "GET" && path.startsWith("/conversations/$conversation/messages?topic=") -> jsonReply("""{"messages":[],"hasMore":false}""")
                call.method == "GET" && path == "/conversations/$direct/messages" -> jsonReply("""{"messages":[],"hasMore":false}""")
                call.method == "POST" && path == "/conversations/$conversation/topic-messages" -> HttpReply(201, """{"messageId":"$ballot","conversationId":"$conversation","senderId":"$user","senderName":"Аня","body":"Тема","createdAt":"2026-09-25T12:00:00Z"}""".toByteArray())
                call.method == "POST" && path == "/conversations/$direct/media" -> HttpReply(201, """{"messageId":"$ballot","conversationId":"$direct","senderId":"$user","senderName":"Аня","body":"photo.png","createdAt":"2026-09-25T12:00:00Z","kind":"image"}""".toByteArray())
                call.method == "POST" && path == "/conversations/$conversation/read" -> jsonReply("""{"conversationId":"$conversation","kind":"group","communityId":"$community","title":"O3313","peerUserId":null,"lastBody":null,"lastAt":null,"unread":0}""")
                call.method == "POST" && path == "/conversations/$direct/read" -> jsonReply("""{"conversationId":"$direct","kind":"direct","communityId":"$community","title":"Личный чат","peerUserId":"$newTopic","lastBody":null,"lastAt":null,"unread":0}""")
                call.method == "GET" && path == "/$community/ballots?topic=$ballotTopic" -> jsonReply(board(chosen))
                call.method == "GET" && path == "/$community/ballots" -> jsonReply(board(chosen))
                call.method == "POST" && path == "/$community/ballots/$ballot/votes" -> { chosen = true; jsonReply(board(chosen)) }
                else -> error("Unexpected ${call.method} $path")
            }
        }
    }
    private fun topicList(canManage: Boolean, includeNew: Boolean = false, restrictedChat: Boolean = false,
        pinnedBallot: Boolean = false, restrictedBallot: Boolean = false) = """{"topics":[${topic(null, "Общий", "chat")},${topic(chatTopic, "Учёба", "chat", canPost = !restrictedChat, policy = if (restrictedChat) "managers" else "all")},${topic(ballotTopic, "Голосования", "ballots", pinned = pinnedBallot, canPost = !restrictedBallot, policy = if (restrictedBallot) "managers" else "all")}${if (includeNew) ",${topic(newTopic, "Консультации", "chat")}" else ""}],"canManageChannels":$canManage}"""
    private fun desk(headman: Boolean, roleCreated: Boolean, powerEnabled: Boolean, granted: Boolean) = """{"headman":$headman,"roles":${if (roleCreated) "[{\"roleId\":\"$trustedRole\",\"name\":\"Доверенные\"}]" else "[]"},"grants":${if (granted) "[{\"roleId\":\"$trustedRole\",\"userId\":\"$newTopic\"}]" else "[]"},"applicants":[],"powers":${if (powerEnabled) "[{\"roleId\":\"$trustedRole\",\"power\":\"channels\"}]" else "[]"},"mine":[]}"""
    private fun topic(id: String?, title: String, kind: String, pinned: Boolean = false,
        canPost: Boolean = true, policy: String = "all") = """{"topicId":${id?.let { "\"$it\"" } ?: "null"},"title":"$title","icon":"💬","kind":"$kind","lastBody":null,"lastAuthor":null,"lastAt":null,"unread":0,"canDelete":false,"activeBallots":0,"description":"","accent":"default","pinned":$pinned,"writePolicy":"$policy","canPost":$canPost}"""
    private fun board(chosen: Boolean) = """{"headman":false,"canOpen":false,"canClose":false,"members":2,"supportersNeeded":1,"ballots":[{"ballotId":"$ballot","question":"Когда?","origin":"headman","status":"open","deadlineAt":"2026-10-01T12:00:00Z","supporters":0,"supportersNeeded":1,"supported":false,"options":[{"optionId":"$option","label":"Завтра","votes":1,"chosen":$chosen}],"effect":"","outcome":"","topicId":"$ballotTopic"}]}"""
}
