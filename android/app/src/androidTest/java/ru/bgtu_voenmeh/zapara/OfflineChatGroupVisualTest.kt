package ru.bgtu_voenmeh.zapara

import android.view.WindowManager
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.hasAnyAncestor
import androidx.compose.ui.test.hasTestTag
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.onAllNodesWithTag
import androidx.compose.ui.test.junit4.createEmptyComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTextReplacement
import androidx.compose.ui.unit.Density
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.espresso.IdlingPolicies
import androidx.test.uiautomator.UiDevice
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.communities.Ballot
import ru.bgtu_voenmeh.zapara.data.communities.BallotBoard
import ru.bgtu_voenmeh.zapara.data.communities.BallotOption
import ru.bgtu_voenmeh.zapara.data.communities.GroupCapabilities
import ru.bgtu_voenmeh.zapara.data.communities.GroupDesk
import ru.bgtu_voenmeh.zapara.data.communities.GroupPower
import ru.bgtu_voenmeh.zapara.data.communities.GroupRole
import ru.bgtu_voenmeh.zapara.data.communities.GroupSpace
import ru.bgtu_voenmeh.zapara.data.communities.GroupTopic
import ru.bgtu_voenmeh.zapara.data.social.InboxRow
import ru.bgtu_voenmeh.zapara.data.social.InboxSource
import ru.bgtu_voenmeh.zapara.data.social.SocialMessage
import ru.bgtu_voenmeh.zapara.ui.groups.GroupCommunityUi
import ru.bgtu_voenmeh.zapara.ui.groups.GroupEvent
import ru.bgtu_voenmeh.zapara.ui.groups.GroupMessageUi
import ru.bgtu_voenmeh.zapara.ui.groups.GroupPersonUi
import ru.bgtu_voenmeh.zapara.ui.groups.GroupSection
import ru.bgtu_voenmeh.zapara.ui.groups.GroupSpaceAction
import ru.bgtu_voenmeh.zapara.ui.groups.GroupUiState
import ru.bgtu_voenmeh.zapara.ui.groups.TopicCreationDraft
import ru.bgtu_voenmeh.zapara.ui.inbox.InboxEvent
import ru.bgtu_voenmeh.zapara.ui.inbox.InboxSection
import ru.bgtu_voenmeh.zapara.ui.inbox.InboxUiState
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.Section
import ru.bgtu_voenmeh.zapara.ui.shell.ShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ZAppScaffold
import ru.bgtu_voenmeh.zapara.ui.shell.ZBottomBar
import ru.bgtu_voenmeh.zapara.ui.theme.MotionSettings
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme
import java.time.Instant
import java.util.concurrent.TimeUnit

/** Production chat/group composables with synthetic data and local callbacks only. */
class OfflineChatGroupVisualTest {
    @get:Rule val rule = createEmptyComposeRule()
    private var host: OwnedTestHost? = null

    @org.junit.Before fun boundOfflineFixtureWaits() {
        IdlingPolicies.setMasterPolicyTimeout(30, TimeUnit.SECONDS)
        IdlingPolicies.setIdlingResourceTimeout(20, TimeUnit.SECONDS)
    }

    @After fun closeHost() {
        host?.close()
        host = null
    }

    @Test fun light_inbox_filters_personal_chat_and_composer() {
        val events = mutableListOf<InboxEvent>()
        val groupOpens = mutableListOf<Pair<String, String>>()
        val current = OwnedTestHost.launch().also { host = it }
        current.scenario.onActivity { activity ->
            activity.window.setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE)
            activity.setContent {
                var state by androidx.compose.runtime.remember { mutableStateOf(inboxFixture()) }
                OfflineFrame(ThemeChoice.Light, conversation = state.active != null) {
                    InboxSection(state, onEvent = { event ->
                        events += event
                        state = when (event) {
                            is InboxEvent.Open -> state.copy(active = event.row, historyLoaded = true)
                            InboxEvent.Back -> state.copy(active = null)
                            is InboxEvent.Draft -> state.copy(composer = state.composer.type(event.value))
                            is InboxEvent.Reply -> state.copy(composer = state.composer.replyTo(event.message))
                            is InboxEvent.LoadMedia -> state.copy(mediaErrors = state.mediaErrors + "fake-photo",
                                mediaNotFound = state.mediaNotFound + "fake-photo")
                            InboxEvent.CancelCompose -> state.copy(composer = state.composer.cancel())
                            else -> state
                        }
                    }, onOpenGroup = { communityId, conversationId -> groupOpens += communityId to conversationId })
                }
            }
        }
        rule.waitForIdle()
        rule.onNodeWithTag("Inbox.Chat.room").assertIsDisplayed()
        // #108 / AN-13: фильтры чатов показываются только от 10 чатов; здесь их меньше.
        assertTrue(rule.onAllNodesWithTag("Inbox.Filters").fetchSemanticsNodes().isEmpty())
        frame(current, "fullqa-chat-light-inbox-collapsed")
        rule.onNodeWithTag("Inbox.Search").performTextReplacement("нет такого чата")
        rule.onNodeWithTag("Empty.InboxSearch").assertIsDisplayed()
        frame(current, "fullqa-chat-light-empty-search")
        rule.onNodeWithTag("Inbox.Reset").performClick()
        rule.onNodeWithTag("Inbox.Chat.room").performClick()
        rule.runOnIdle { assertEquals(listOf("offline-group" to "room"), groupOpens) }
        rule.onNodeWithTag("Inbox.Chat.friend").performClick()
        rule.onNodeWithTag("Inbox.Draft").assertIsDisplayed()
        assertTrue("History search starts collapsed",
            rule.onAllNodesWithTag("Inbox.HistorySearch").fetchSemanticsNodes().isEmpty())
        rule.onNodeWithTag("Inbox.HistorySearchToggle").performClick()
        val searchBottom = rule.onNodeWithTag("Inbox.HistorySearch").fetchSemanticsNode().boundsInRoot.bottom
        val filterTop = rule.onNodeWithTag("Inbox.HistoryFilters").fetchSemanticsNode().boundsInRoot.top
        val density = current.activity.resources.displayMetrics.density
        assertTrue("History filters need an 8dp gap below search", filterTop - searchBottom >= 8 * density - 1f)
        frame(current, "fullqa-chat-light-personal")
        rule.onNodeWithTag("Inbox.HistoryFilters").performClick()
        rule.onNodeWithTag("Inbox.HistoryAuthor.Mine").performClick()
        rule.onNodeWithTag("Inbox.HistoryFiltersDone").assertIsDisplayed().performClick()
        rule.onNodeWithTag("Inbox.HistoryFilters").assertIsSelected()
        frame(current, "fullqa-chat-light-history-filter-active")
        rule.onNodeWithTag("Inbox.HistoryReset").performClick()
        val actions = rule.onNodeWithTag("Inbox.MessageActions.message-2").performScrollTo().assertIsDisplayed()
        val actionBounds = actions.fetchSemanticsNode().boundsInRoot
        assertTrue("Message action must retain a 48dp touch target",
            actionBounds.width >= 48 * density - 1f && actionBounds.height >= 48 * density - 1f)
        actions.performClick()
        rule.onNodeWithTag("Inbox.CopyMessage").assertIsDisplayed()
        frame(current, "fullqa-chat-light-message-actions")
        UiDevice.getInstance(InstrumentationRegistry.getInstrumentation()).pressBack()
        rule.onNodeWithTag("Inbox.Message.message-photo").performScrollTo().assertIsDisplayed()
        rule.onNodeWithTag("Chat.MediaLoad").assertIsDisplayed()
        frame(current, "fullqa-chat-light-attachment-bubble")
        rule.onNodeWithTag("Chat.MediaLoad").performClick()
        rule.onNodeWithTag("Chat.MediaRetry").performScrollTo().assertIsDisplayed()
        rule.onNodeWithText("Вложение недоступно на сервере").assertIsDisplayed()
        frame(current, "fullqa-chat-light-attachment-not-found")
        rule.onNodeWithTag("Inbox.MessageActions.message-photo").performScrollTo().performClick()
        rule.onNodeWithTag("Inbox.SaveMessage.message-photo").assertIsDisplayed()
        frame(current, "fullqa-chat-light-attachment-actions")
        UiDevice.getInstance(InstrumentationRegistry.getInstrumentation()).pressBack()
        rule.onNodeWithTag("Inbox.Draft").performTextReplacement("Локальный черновик")
        rule.onNodeWithTag("Inbox.Send").assertIsDisplayed()
        frame(current, "fullqa-chat-light-draft")
        rule.onNodeWithTag("Inbox.Attach").performClick()
        rule.onNodeWithTag("Inbox.File").assertIsDisplayed()
        frame(current, "fullqa-chat-light-attachment-menu")
        rule.runOnIdle {
            assertTrue(events.any { it is InboxEvent.Open && it.row.id == "friend" })
            assertTrue(events.any { it is InboxEvent.Draft && it.value == "Локальный черновик" })
            assertTrue(events.none { it == InboxEvent.Send || it == InboxEvent.Invite || it is InboxEvent.Delete })
            assertTrue(events.count { it is InboxEvent.LoadMedia && it.message.id == "message-photo" } == 1)
            assertTrue(events.none { it is InboxEvent.Save })
        }
    }

    @Test fun dark_group_list_people_chat_details_search_and_ballot() {
        val events = mutableListOf<GroupEvent>()
        val current = OwnedTestHost.launch().also { host = it }
        current.scenario.onActivity { activity ->
            activity.setContent {
                var state by androidx.compose.runtime.remember { mutableStateOf(groupFixture().copy(hasHome = false)) }
                OfflineFrame(ThemeChoice.Dark, conversation = state.hasHome && !state.showChannels && !state.showPeople) {
                    GroupSection(state, onEvent = { event ->
                        events += event
                        state = when (event) {
                            is GroupEvent.Open -> groupFixture().copy(showChannels = true)
                            GroupEvent.People -> state.copy(showPeople = true, showChannels = false)
                            GroupEvent.Chat, GroupEvent.Channels, GroupEvent.Back -> state.copy(showPeople = false, showChannels = true)
                            GroupEvent.GroupChat -> state.copy(showPeople = false, showChannels = false,
                                activeTopicId = null, activeChannelKind = "chat", activeConversationId = "room", chatTitle = "Общий чат")
                            is GroupEvent.OpenChannel -> state.copy(showPeople = false, showChannels = false,
                                activeTopicId = event.topicId, activeChannelKind = if (event.topicId == "ballots") "ballots" else "chat",
                                activeConversationId = "room", chatTitle = if (event.topicId == "ballots") "Голосования" else "Общий чат")
                            is GroupEvent.Draft -> state.copy(draft = event.text)
                            else -> state
                        }
                    })
                }
            }
        }
        rule.waitForIdle()
        rule.onNodeWithTag("Group.Open.offline-group").assertIsDisplayed()
        frame(current, "fullqa-group-dark-list")
        rule.onNodeWithTag("Group.Open.offline-group").performClick()
        rule.onNodeWithTag("Group.Channel.ballots").performScrollTo().assertIsDisplayed()
        assertTrue("Channel filters start collapsed",
            rule.onAllNodesWithTag("Group.ChannelKinds").fetchSemanticsNodes().isEmpty())
        assertTrue("Group tools start collapsed",
            rule.onAllNodesWithTag("Group.Obligations").fetchSemanticsNodes().isEmpty())
        rule.onNodeWithTag("Group.Info").performClick()
        rule.onNodeWithText(current.activity.getString(R.string.group_disclaimer)).assertIsDisplayed()
        UiDevice.getInstance(InstrumentationRegistry.getInstrumentation()).pressBack()
        frame(current, "fullqa-group-dark-channels")
        rule.onNodeWithTag("Group.Panes.1").performClick()
        rule.onNodeWithTag("Group.Room").assertIsDisplayed()
        frame(current, "fullqa-group-dark-people")
        rule.onNodeWithTag("Group.Room").performClick()
        rule.onNodeWithTag("Group.Draft").assertIsDisplayed()
        frame(current, "fullqa-group-dark-chat")
        rule.onNodeWithTag("Group.Details").performClick()
        rule.onNodeWithTag("Group.DetailsSheet").assertIsDisplayed()
        frame(current, "fullqa-group-dark-details")
        UiDevice.getInstance(InstrumentationRegistry.getInstrumentation()).pressBack()
        rule.onNodeWithTag("Group.MessageFilters").performClick()
        rule.onNodeWithTag("Group.MessageSearch").performTextReplacement("Учебное сообщение 2")
        frame(current, "fullqa-group-dark-search")
        rule.onNodeWithTag("Group.Back").performClick()
        rule.onNodeWithTag("Group.Channel.ballots").performScrollTo().performClick()
        rule.onNodeWithTag("Group.Ballot.ballot-1").assertIsDisplayed()
        frame(current, "fullqa-group-dark-ballot")
        rule.runOnIdle {
            assertTrue(events.contains(GroupEvent.Open("offline-group")))
            assertTrue(events.contains(GroupEvent.People))
            assertTrue(events.contains(GroupEvent.GroupChat))
            assertTrue(events.contains(GroupEvent.OpenChannel("ballots")))
            assertTrue(events.none { it == GroupEvent.Send || it is GroupEvent.VoteBallot || it is GroupEvent.SpaceAction })
        }
    }

    @Test fun large_text_keeps_group_navigation_and_chat_reachable() {
        val events = mutableListOf<GroupEvent>()
        val noUnread = groupFixture().let { fixture ->
            fixture.copy(channels = fixture.channels.map { it.copy(unread = 0) })
        }
        val current = OwnedTestHost.launch().also { host = it }
        current.scenario.onActivity { activity ->
            activity.setContent {
                var state by androidx.compose.runtime.remember { mutableStateOf(noUnread.copy(showChannels = true)) }
                OfflineFrame(ThemeChoice.Light, fontScale = 2f, conversation = !state.showChannels && !state.showPeople) {
                    GroupSection(state, onEvent = { event ->
                        events += event
                        state = when (event) {
                            GroupEvent.People -> state.copy(showPeople = true, showChannels = false)
                            GroupEvent.GroupChat -> state.copy(showPeople = false, showChannels = false)
                            else -> state
                        }
                    })
                }
            }
        }
        rule.waitForIdle()
        rule.onNodeWithTag("Nav.Chat").assertIsDisplayed()
        assertTrue("No disabled unread action occupies the group header",
            rule.onAllNodesWithTag("Group.NextUnread").fetchSemanticsNodes().isEmpty())
        rule.onNodeWithTag("Group.Panes.1").assertIsDisplayed().performClick()
        frame(current, "fullqa-group-large-text-people")
        val device = UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
        repeat(3) {
            if (runCatching { rule.onNodeWithTag("Group.Room").assertIsDisplayed() }.isSuccess) return@repeat
            device.swipe(device.displayWidth / 2, device.displayHeight * 78 / 100,
                device.displayWidth / 2, device.displayHeight * 48 / 100, 24)
            rule.waitForIdle()
        }
        rule.onNodeWithTag("Group.Room").assertIsDisplayed()
        frame(current, "fullqa-group-large-text-room")
        rule.onNodeWithTag("Group.Room").performClick()
        rule.onNodeWithTag("Group.Draft").assertIsDisplayed()
        frame(current, "fullqa-group-large-text-chat")
        rule.runOnIdle { assertTrue(events.contains(GroupEvent.People) && events.contains(GroupEvent.GroupChat)) }
    }

    @Test fun offline_headman_can_browse_roles_and_channel_templates() {
        val events = mutableListOf<GroupEvent>()
        val role = GroupRole("curator", "Куратор", position = 10, icon = "🛡️")
        val capabilities = GroupCapabilities(powers = listOf("channels", "roles"),
            templates = listOf("chat", "announcements", "polls", "forms", "subject", "materials", "homework", "schedule"))
        val desk = GroupDesk(headman = true, roles = listOf(role), grants = emptyList(),
            powers = listOf(GroupPower("curator", "channels")), mine = listOf("roles", "channels", "access"),
            capabilities = capabilities)
        val space = GroupSpace(groupFixture().channels, emptyList(), capabilities, desk)
        val current = OwnedTestHost.launch().also { host = it }
        current.scenario.onActivity { activity ->
            activity.setContent {
                var state by androidx.compose.runtime.remember {
                    mutableStateOf(groupFixture().copy(ownerId = "me", myRole = "headman", desk = desk, space = space,
                        canManageChannels = true, showChannels = true))
                }
                OfflineFrame(ThemeChoice.Light, conversation = false) {
                    GroupSection(state, onEvent = { event ->
                        events += event
                        state = when (event) {
                            is GroupEvent.SpaceAction -> when (val action = event.action) {
                                is GroupSpaceAction.Panel -> state.copy(spacePanel = action.name)
                                else -> state
                            }
                            GroupEvent.BeginCreation -> state.copy(creationDraft = TopicCreationDraft("offline-group", "me"))
                            is GroupEvent.CreationChanged -> state.copy(creationDraft = event.draft)
                            GroupEvent.CancelCreation -> state.copy(creationDraft = null)
                            else -> state
                        }
                    })
                }
            }
        }
        rule.waitForIdle()
        assertTrue("Management actions start collapsed",
            rule.onAllNodesWithTag("Group.ChannelManage").fetchSemanticsNodes().isEmpty())
        rule.onNodeWithTag("Group.ToolsToggle").performScrollTo().performClick()
        rule.onNodeWithText("Роли").performScrollTo().performClick()
        rule.onNodeWithTag("Group.Space.roles").assertIsDisplayed()
        frame(current, "fullqa-group-roles")
        rule.onNodeWithText("Куратор", substring = true).performClick()
        rule.onNodeWithText("Настройки").assertIsDisplayed()
        frame(current, "fullqa-group-rolesedit")
        UiDevice.getInstance(InstrumentationRegistry.getInstrumentation()).pressBack()
        rule.onNodeWithTag("Group.ChannelManage").performScrollTo().performClick()
        rule.onNodeWithTag("Group.ChannelCreate").performScrollTo().performClick()
        rule.onNodeWithTag("Group.TopicCreation").assertIsDisplayed()
        frame(current, "fullqa-group-new-channel-form-top")
        val pollsTemplate = hasText("Опросы") and hasAnyAncestor(hasTestTag("Group.TopicCreation"))
        rule.onNode(pollsTemplate, useUnmergedTree = true).performScrollTo().assertIsDisplayed()
        frame(current, "fullqa-group-new-channel-types")
        rule.onNode(pollsTemplate, useUnmergedTree = true).performClick()
        frame(current, "fullqa-group-new-channel-polls")
        rule.runOnIdle {
            assertTrue(events.contains(GroupEvent.SpaceAction(GroupSpaceAction.Panel("roles"))))
            assertTrue(events.contains(GroupEvent.BeginCreation))
            assertTrue(events.any { it is GroupEvent.CreationChanged && it.draft.template == "polls" })
            assertTrue(events.none { it == GroupEvent.SubmitCreation || it is GroupEvent.CreateChannel ||
                it is GroupEvent.CreateTrustedRole || it is GroupEvent.GrantTrusted })
        }
    }

    private fun frame(current: OwnedTestHost, name: String) {
        rule.waitForIdle()
        assertTrue(Frames.capture(current.activity, name).length() > 1000)
    }

    @androidx.compose.runtime.Composable
    private fun OfflineFrame(theme: ThemeChoice, fontScale: Float = 1f, conversation: Boolean,
        content: @androidx.compose.runtime.Composable () -> Unit) {
        ZaparaTheme(theme, MotionSettings.Off) {
            val density = LocalDensity.current.density
            CompositionLocalProvider(LocalDensity provides Density(density, fontScale),
                LocalShellChrome provides ShellChrome("А863С", false, true) {}) {
                ZAppScaffold(conversation = conversation,
                    bottomBar = { ZBottomBar(Section.Chat, false, 0, false, {}, {}) }) {
                    androidx.compose.foundation.layout.Box(Modifier.background(Zapara.colors.canvas)) { content() }
                }
            }
        }
    }

    private fun inboxFixture(): InboxUiState {
        val time = Instant.parse("2026-09-28T12:00:00Z")
        return InboxUiState(userId = "me", profileDatabaseName = "offline-test", inboxLoaded = true,
            rows = listOf(
                InboxRow("room", "А863С · Общий чат", "offline-group", "Учебное сообщение", time, 3),
                InboxRow("friend", "Одногруппник", lastBody = "Привет!", lastAt = time.plusSeconds(60),
                    unread = 1, source = InboxSource.Friend)),
            messages = listOf(
                SocialMessage("message-1", "friend", "Одногруппник", "Привет!", "text", time,
                    null, null, false, false, true, emptyList(), null, null),
                SocialMessage("message-photo", "friend", "Одногруппник", null, "image", time.plusSeconds(30),
                    null, null, false, false, true, emptyList(), "fake-photo", "photo.jpg"),
                SocialMessage("message-2", "me", "Вы", "Увидимся после пары", "text", time.plusSeconds(60),
                    "message-1", "Привет!", false, false, true, emptyList(), null, null)))
    }

    private fun groupFixture(): GroupUiState {
        val time = Instant.parse("2026-09-28T12:00:00Z")
        return GroupUiState(communities = listOf(GroupCommunityUi("offline-group", "А863С · Учебная группа", "member")),
            hasHome = true, communityId = "offline-group", title = "А863С · Учебная группа", myRole = "member",
            showChannels = true, canPost = true, chatTitle = "Общий чат", activeConversationId = "room",
            channels = listOf(
                GroupTopic(null, "Общий чат", "💬", "chat", "Учебное сообщение", "Одногруппник", time, 3, false, 0),
                GroupTopic("ballots", "Голосования", "📊", "ballots", "Выбор даты", "Староста", time, 1, false, 1)),
            people = listOf(GroupPersonUi("me", "Вы", "me", "member", true),
                GroupPersonUi("friend", "Одногруппник", "friend", "member", false)),
            messages = listOf(GroupMessageUi("message-1", "Одногруппник", "Учебное сообщение 1", "12:00", false),
                GroupMessageUi("message-2", "Вы", "Учебное сообщение 2", "12:01", true)),
            board = BallotBoard(false, false, false, 25, 3, listOf(
                Ballot("ballot-1", "Когда удобно встретиться?", "Староста", "open", time.plusSeconds(86_400),
                    3, 3, true, listOf(BallotOption("option-1", "Во вторник", 8, false),
                        BallotOption("option-2", "В среду", 12, true)), "", "", "ballots"))))
    }
}
