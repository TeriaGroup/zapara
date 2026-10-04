package ru.bgtu_voenmeh.zapara

import android.graphics.Rect
import android.view.WindowManager
import androidx.activity.compose.setContent
import androidx.compose.runtime.*
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createEmptyComposeRule
import androidx.compose.ui.unit.Density
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.espresso.IdlingPolicies
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.UiDevice
import org.junit.Assert.*
import org.junit.After
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import ru.bgtu_voenmeh.zapara.data.communities.GroupTopic
import ru.bgtu_voenmeh.zapara.data.social.InboxRow
import ru.bgtu_voenmeh.zapara.data.social.SocialMessage
import ru.bgtu_voenmeh.zapara.ui.groups.*
import ru.bgtu_voenmeh.zapara.ui.inbox.*
import ru.bgtu_voenmeh.zapara.ui.shell.*
import ru.bgtu_voenmeh.zapara.ui.theme.*
import org.json.JSONObject
import java.io.File
import java.time.Instant
import java.time.LocalDate
import java.util.concurrent.TimeUnit

/** Offline production screens. No AppContainer, account, API, media or database operations. */
@RunWith(AndroidJUnit4::class)
class ChatKeyboardLayoutTest {
    @get:Rule val rule = createEmptyComposeRule()
    private val instrumentation get() = InstrumentationRegistry.getInstrumentation()
    private var host: OwnedTestHost? = null
    private val activity get() = requireNotNull(host).activity

    @Before fun boundOfflineFixtureWaits() {
        IdlingPolicies.setMasterPolicyTimeout(30, TimeUnit.SECONDS)
        IdlingPolicies.setIdlingResourceTimeout(20, TimeUnit.SECONDS)
        host = OwnedTestHost.launch()
    }

    @After fun closeHost() {
        try { host?.close() } finally { host = null }
    }

    private fun screen(group: Boolean, context: Boolean = false, fontScale: Float = 1f) {
        requireNotNull(host).scenario.onActivity { current ->
            current.window.setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE)
            current.setContent {
            var inbox by remember { mutableStateOf(personalFixture(context)) }
            var space by remember { mutableStateOf(groupFixture(context)) }
            ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
                val density = LocalDensity.current.density
                CompositionLocalProvider(LocalDensity provides Density(density, fontScale),
                    LocalShellChrome provides ShellChrome("А863С", false, true) {}) {
                    ZAppScaffold(conversation = true,
                        bottomBar = { ZBottomBar(Section.Chat, false, 0, false, {}, {}) }) {
                            if (group) GroupSection(space, { event ->
                                space = when (event) {
                                    is GroupEvent.Draft -> space.copy(draft = event.text)
                                    GroupEvent.CancelContext -> space.copy(replyTo = null, editing = null)
                                    is GroupEvent.Context -> space.copy(composeContext = event.text)
                                    else -> space
                                }
                            })
                            else InboxSection(inbox, { event ->
                                inbox = when (event) {
                                    is InboxEvent.Draft -> inbox.copy(composer = inbox.composer.type(event.value))
                                    InboxEvent.CancelCompose -> inbox.copy(composer = inbox.composer.cancel())
                                    else -> inbox
                                }
                            }, { _, _ -> })
                    }
                }
            }
            }
        }
        rule.waitForIdle()
    }

    @Test fun personal_typing_keeps_input_and_send_above_keyboard() = exercise(false)
    @Test fun group_with_context_keeps_input_and_send_above_keyboard() = exercise(true)
    @Test fun personal_reply_and_error_keep_multiline_input_reachable() = exercise(false, true)
    @Test fun group_reply_and_error_keep_multiline_input_reachable() = exercise(true, true)
    @Test fun personal_older_history_retains_position_while_typing() = olderHistory(false)
    @Test fun group_older_history_retains_position_while_typing() = olderHistory(true)

    @Test fun group_search_and_context_are_reachable_without_consuming_history_height() {
        screen(true)
        rule.onNodeWithTag("Group.Details").performClick()
        capture("Group-details")
        rule.onNodeWithTag("Group.Context", useUnmergedTree = true).performScrollTo().assertIsDisplayed()
        UiDevice.getInstance(instrumentation).pressBack()
        rule.waitForIdle()
        rule.onNodeWithTag("Group.MessageFilters").performClick()
        rule.onNodeWithTag("Group.MessageSearch").performClick().performTextReplacement("сообщение 12")
        rule.waitUntil(8_000) { KeyboardEvidence.visible() }
        capture("Group-search-keyboard")
        assertVisibleInWindow("Group.MessageFiltersDone")
        rule.onNodeWithTag("Group.MessageFiltersDone").performClick()
        rule.waitForIdle()
        capture("Group-search-results")
        rule.onNodeWithTag("Group.SearchActive").assertIsDisplayed()
        rule.onNodeWithTag("Group.Message.message-12").assertIsDisplayed()
        rule.onNodeWithTag("Group.Draft").assertIsDisplayed()
    }

    @Test fun group_search_done_remains_reachable_with_keyboard_at_large_text() {
        screen(true, fontScale = 2f)
        rule.onNodeWithTag("Group.MessageFilters").performClick()
        val search = rule.onNodeWithTag("Group.MessageSearch")
        search.performClick()
        capture("Group-search-large-focused")
        rule.waitUntil(8_000) { KeyboardEvidence.visible() }
        search.performTextReplacement("сообщение")
        KeyboardEvidence.requireVisible()
        assertVisibleInWindow("Group.MessageFiltersDone")
        rule.onNodeWithTag("Group.MessageFiltersDone").performClick()
        rule.onNodeWithTag("Group.SearchActive").assertIsDisplayed()
    }

    private fun olderHistory(group: Boolean) {
        val prefix = if (group) "Group" else "Inbox"
        screen(group)
        rule.onNodeWithTag("$prefix.Messages").performScrollToIndex(3)
        rule.waitForIdle()
        val anchor = rule.onNodeWithTag("$prefix.Message.message-4")
        val top = anchor.assertIsDisplayed().fetchSemanticsNode().boundsInWindow.top
        rule.onNodeWithTag("$prefix.Draft").performClick()
        rule.waitUntil(8_000) { KeyboardEvidence.visible() }
        rule.onNodeWithTag("$prefix.Draft").performTextReplacement("Первая строка\nВторая строка\nТретья строка\nЧетвёртая строка")
        rule.waitForIdle()
        capture("$prefix-older-history-keyboard")
        assertEquals("Typing must preserve the older message's position", top,
            anchor.assertIsDisplayed().fetchSemanticsNode().boundsInWindow.top, 2f)
        assertVisibleInWindow("$prefix.Draft")
    }

    private fun exercise(group: Boolean, context: Boolean = false) {
        val prefix = if (group) "Group" else "Inbox"
        val name = "$prefix-${if (context) "reply-error" else "ordinary"}"
        screen(group, context)
        capture("$name-loaded")
        val field = rule.onNodeWithTag("$prefix.Draft")
        field.assertIsDisplayed().performClick()
        rule.waitUntil(8_000) { KeyboardEvidence.visible() }
        capture("$name-keyboard")
        assertVisibleInWindow("$prefix.Draft")
        for (count in listOf(1, 4, 20)) {
            val draft = (1..count).joinToString("\n") { "Строка $it для проверки ввода" }
            field.performTextReplacement(draft)
            rule.waitForIdle()
            KeyboardEvidence.requireVisible()
            capture("$name-$count-lines")
            assertVisibleInWindow("$prefix.Draft")
            assertVisibleInWindow("$prefix.Send")
            rule.onNodeWithTag("$prefix.Message.message-24").assertIsDisplayed()
            if (group) assertEquals("Group editor text must equal the typed draft", draft,
                field.fetchSemanticsNode().config[SemanticsProperties.EditableText].text)
            else field.assertTextEquals(draft)
            val selection = field.fetchSemanticsNode().config[SemanticsProperties.TextSelectionRange]
            assertEquals("Input connection selection must follow the last typed character", draft.length, selection.end)
        }
        rule.onNodeWithTag("$prefix.Attach").performClick()
        capture("$name-attachment-menu")
        rule.onNodeWithTag("$prefix.Circle").assertIsDisplayed()
        UiDevice.getInstance(instrumentation).pressBack()
        rule.waitForIdle()
        instrumentation.runOnMainSync {
            activity.window.decorView.clearFocus()
            val manager = activity.getSystemService(android.content.Context.INPUT_METHOD_SERVICE) as android.view.inputmethod.InputMethodManager
            manager.hideSoftInputFromWindow(activity.window.decorView.windowToken, 0)
        }
        rule.waitUntil(8_000) { !KeyboardEvidence.visible() }
        capture("$name-keyboard-closed")
        assertVisibleInWindow("$prefix.Draft")
    }

    private fun assertVisibleInWindow(tag: String) {
        val node = rule.onNodeWithTag(tag).assertIsDisplayed()
        val bounds = node.fetchSemanticsNode().boundsInWindow
        val visible = Rect()
        val windowOrigin = IntArray(2)
        instrumentation.runOnMainSync {
            activity.window.decorView.getWindowVisibleDisplayFrame(visible)
            activity.window.decorView.getLocationOnScreen(windowOrigin)
        }
        assertTrue("$tag must have a usable height: $bounds", bounds.height >= 40 * rule.density.density)
        assertTrue("$tag top clipped: $bounds, visible=$visible", bounds.top + windowOrigin[1] >= visible.top - 1)
        assertTrue("$tag below keyboard: $bounds, visible=$visible", bounds.bottom + windowOrigin[1] <= visible.bottom + 1)
        val full = node.getUnclippedBoundsInRoot()
        assertEquals("$tag clipped inside its layout", (full.bottom - full.top).value * rule.density.density, bounds.height, 1f)
    }

    /** Opt-in only: -e chatEvidence true. Root agent owns running and reading these captures. */
    private fun capture(name: String) {
        if (InstrumentationRegistry.getArguments().getString("chatEvidence") != "true") return
        rule.waitForIdle()
        Thread.sleep(250)
        val directory = File(activity.getExternalFilesDir(null), "chat-keyboard-evidence").apply { mkdirs() }
        val device = UiDevice.getInstance(instrumentation)
        assertTrue("Full-screen capture failed: $name", device.takeScreenshot(File(directory, "$name.png")))
        val visible = Rect()
        val origin = IntArray(2)
        instrumentation.runOnMainSync {
            activity.window.decorView.getWindowVisibleDisplayFrame(visible)
            activity.window.decorView.getLocationOnScreen(origin)
        }
        val nodes = JSONObject()
        listOf("Inbox.Draft", "Inbox.Send", "Inbox.Message.message-24",
            "Group.Draft", "Group.Send", "Group.Message.message-24",
            "Group.MessageSearch", "Group.MessageFiltersDone").forEach { tag ->
            rule.onAllNodesWithTag(tag).fetchSemanticsNodes().firstOrNull()?.let { node ->
                val bounds = node.boundsInWindow
                nodes.put(tag, JSONObject().put("left", bounds.left).put("top", bounds.top)
                    .put("right", bounds.right).put("bottom", bounds.bottom)
                    .put("screen_top", bounds.top + origin[1]).put("screen_bottom", bounds.bottom + origin[1]))
            }
        }
        File(directory, "$name.json").writeText(JSONObject()
            .put("image", "$name.png").put("screen_width", device.displayWidth)
            .put("screen_height", device.displayHeight).put("keyboard_visible", KeyboardEvidence.visible())
            .put("visible_top", visible.top).put("visible_bottom", visible.bottom)
            .put("window_origin_x", origin[0]).put("window_origin_y", origin[1])
            .put("nodes", nodes).toString(2))
    }

    private fun personalFixture(context: Boolean): InboxUiState {
        val messages = (1..24).map { index -> SocialMessage("message-$index", if (index % 2 == 0) "me" else "friend",
            if (index % 2 == 0) "Вы" else "Одногруппник", "Учебное сообщение $index", "text",
            Instant.parse("2026-09-28T12:00:00Z").plusSeconds(index.toLong()), null, null,
            false, false, true, emptyList(), null, null) }
        return InboxUiState(active = InboxRow("offline-personal", "Одногруппник"), userId = "me",
            messages = messages, historyLoaded = true,
            composer = PersonalComposer(reply = messages.first().takeIf { context },
                error = "Не удалось отправить. Проверьте соединение и повторите.".takeIf { context }))
    }

    private fun groupFixture(context: Boolean) = GroupUiState(hasHome = true, communityId = "offline-group",
        title = "А863С · Учебная группа", myRole = "member", chatTitle = "Общий чат",
        activeConversationId = "offline-conversation", canPost = true,
        channels = listOf(
            GroupTopic(null, "Общий чат", "chat", "chat", null, null, null, 4, false, 0),
            GroupTopic("ballots", "Голосования", "chat", "ballots", null, null, null, 3, false, 2)),
        contextLesson = GroupLessonHint(LocalDate.of(2026, 9, 29), "09:00", "Высшая математика", "301"),
        messages = (1..24).map { GroupMessageUi("message-$it", "Одногруппник", "Учебное сообщение $it", "12:00", it % 2 == 0) },
        replyTo = "message-1".takeIf { context },
        composeContext = "Высшая математика · 29 сентября · Домашнее задание к следующему занятию".takeIf { context },
        failed = context, mediaError = context)
}
