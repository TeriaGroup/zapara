package ru.bgtu_voenmeh.zapara

import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Surface
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.hasAnyAncestor
import androidx.compose.ui.test.hasScrollAction
import androidx.compose.ui.test.hasTestTag
import androidx.compose.ui.test.junit4.createEmptyComposeRule
import androidx.compose.ui.test.onAllNodesWithTag
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performScrollToNode
import androidx.compose.ui.test.performTextReplacement
import androidx.compose.ui.test.performTouchInput
import androidx.compose.ui.test.swipeUp
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.UiDevice
import java.time.LocalDate
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Homework
import ru.bgtu_voenmeh.zapara.ui.AndroidUiCopy
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkCompletionFilter
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkEditorState
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkEvent
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkGroups
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkSection
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkUiState
import ru.bgtu_voenmeh.zapara.ui.homework.SharedHomeworkItemUi
import ru.bgtu_voenmeh.zapara.ui.settings.SettingsEvent
import ru.bgtu_voenmeh.zapara.ui.settings.SettingsSection
import ru.bgtu_voenmeh.zapara.ui.settings.SettingsUiState
import ru.bgtu_voenmeh.zapara.ui.settings.UpdateUiState
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ShellChrome
import ru.bgtu_voenmeh.zapara.ui.theme.MotionSettings
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme

/** Real Compose screens driven by synthetic state; callbacks never open a repository. */
class LearningScreensVisualTest {
    @get:Rule val rule = createEmptyComposeRule()
    private var host: OwnedTestHost? = null
    private var originalFontScale: String? = null

    @After fun closeHost() {
        try {
            host?.close()
        } finally {
            host = null
            originalFontScale?.let(FontScaleReadiness::setAndAwait)
            originalFontScale = null
        }
    }

    private fun launchAtScale(scale: Float): OwnedTestHost {
        val device = UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
        originalFontScale = device.executeShellCommand("settings get system font_scale").trim().also {
            require(it.toFloatOrNull() != null)
        }
        FontScaleReadiness.setAndAwait(scale.toString())
        return OwnedTestHost.launch().also {
            host = it
            FontScaleReadiness.awaitHost(it, scale)
        }
    }

    private fun shot(activity: Api37TestActivity, name: String) {
        rule.waitForIdle()
        assertTrue(Frames.capture(activity, name).length() > 1000)
    }

    @Test fun homework_list_filters_completion_and_editor_use_only_local_state() {
        val current = launchAtScale(1f)
        val today = LocalDate.of(2026, 9, 12)
        var local by mutableStateOf(Homework(7L, "матан", "Прочитать главу и решить задачи",
            today.minusDays(3), 1, today.minusDays(1), "overdue", false))
        var shared by mutableStateOf(SharedHomeworkItemUi("shared-1", "test-community",
            "Физика", "Подготовить доклад", "срок 14.09", false, 0, true, false))
        var filter by mutableStateOf(HomeworkCompletionFilter.All)
        var editor by mutableStateOf<HomeworkEditorState?>(null)
        var composedScale = Float.NaN
        current.scenario.onActivity { activity ->
            activity.setContent {
                val copy = AndroidUiCopy(activity)
                val actualScale = LocalDensity.current.fontScale
                SideEffect { composedScale = actualScale }
                ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
                    CompositionLocalProvider(LocalShellChrome provides ShellChrome("А863С", false, true) {}) {
                        Surface(modifier = Modifier.fillMaxSize(), color = Zapara.colors.canvas) {
                            val group = HomeworkGroups.group(listOf(HomeworkGroups.toItem(local,
                                "Высшая математика", today, copy)), copy).map { it.copy(collapsed = false) }
                            HomeworkSection(HomeworkUiState(loaded = true, hasGroup = true,
                                groups = group, editor = editor, browseFilter = filter,
                                sharedRows = listOf(shared), guest = false)) { event ->
                                when (event) {
                                    is HomeworkEvent.ToggleDone -> local = local.copy(done = !local.done,
                                        status = if (local.done) "overdue" else "done")
                                    is HomeworkEvent.ToggleShared -> shared = shared.copy(completed = !shared.completed)
                                    is HomeworkEvent.BrowseFilter -> filter = event.value
                                    is HomeworkEvent.Edit -> editor = HomeworkEditorState(local.id, local.norm,
                                        "Высшая математика", local.text, local.n, true, { _, _ -> local.due })
                                    HomeworkEvent.Add -> editor = HomeworkEditorState(null, "матан",
                                        "Высшая математика", "", 1, false, { _, _ -> null })
                                    is HomeworkEvent.EditorText -> editor = editor?.withText(event.text)
                                    HomeworkEvent.Cancel, HomeworkEvent.Save -> editor = null
                                    else -> Unit
                                }
                            }
                        }
                    }
                }
            }
        }
        rule.waitForIdle()
        rule.runOnIdle { assertEquals(1f, composedScale) }
        rule.onNodeWithTag("Homework.Row.7").assertIsDisplayed()
        shot(current.activity, "fullqa-learning-list")

        rule.onNodeWithTag("Homework.Done.7").performClick()
        rule.onNodeWithTag("Homework.Filter.Done").performClick()
        rule.onNodeWithTag("Homework.Row.7").assertIsDisplayed()
        shot(current.activity, "fullqa-learning-done-filter")
        rule.onNodeWithTag("Homework.Done.7").performClick()
        rule.onNodeWithTag("Homework.Filter.All").performClick()
        rule.onNode(hasScrollAction()).performScrollToNode(hasTestTag("Homework.Shared.shared-1"))
        rule.onNodeWithTag("Homework.Shared.shared-1").assertIsDisplayed()
        shot(current.activity, "fullqa-learning-shared")

        rule.onNode(hasScrollAction()).performScrollToNode(hasTestTag("Homework.Edit.7"))
        rule.onNodeWithTag("Homework.Edit.7").performClick()
        rule.onNodeWithTag("Editor.Text").assertIsDisplayed()
            .performClick().performTextReplacement("Новый конспект")
        val device = UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
        rule.waitUntil(8_000) {
            device.executeShellCommand("dumpsys input_method").contains("mInputShown=true")
        }
        shot(current.activity, "fullqa-learning-edit-keyboard")
        fun attachmentVisibleAboveFooter(tag: String) = runCatching {
            val action = rule.onNodeWithTag(tag).assertIsDisplayed().fetchSemanticsNode().boundsInRoot
            val footerTop = rule.onNodeWithTag("Editor.Cancel").fetchSemanticsNode().boundsInRoot.top
            action.bottom <= footerTop + 1f
        }.getOrDefault(false)
        val photoInitiallyVisible = attachmentVisibleAboveFooter("Editor.Photo")
        val documentInitiallyVisible = attachmentVisibleAboveFooter("Editor.Document")
        rule.onNodeWithTag("Editor.Photo").performScrollTo()
        val photoImeVisible = device.executeShellCommand("dumpsys input_method").contains("mInputShown=true")
        shot(current.activity, if (photoImeVisible) "fullqa-learning-attachments-keyboard"
            else "fullqa-learning-attachments-no-ime")
        var photoVisible = attachmentVisibleAboveFooter("Editor.Photo")
        var documentVisible = attachmentVisibleAboveFooter("Editor.Document")
        val editorBody = rule.onNode(hasScrollAction() and hasAnyAncestor(hasTestTag("Sheet.Homework")),
            useUnmergedTree = true)
        repeat(2) { attempt ->
            if (!photoVisible || !documentVisible) {
                editorBody.performTouchInput { swipeUp() }
                val imeVisible = device.executeShellCommand("dumpsys input_method").contains("mInputShown=true")
                val suffix = if (imeVisible) "keyboard" else "no-ime"
                shot(current.activity, "fullqa-learning-attachments-swipe${attempt + 1}-$suffix")
                photoVisible = photoVisible || attachmentVisibleAboveFooter("Editor.Photo")
                documentVisible = documentVisible || attachmentVisibleAboveFooter("Editor.Document")
            }
        }
        if (device.executeShellCommand("dumpsys input_method").contains("mInputShown=true")) device.pressBack()
        rule.onNodeWithTag("Editor.Cancel").performClick()
        rule.onNodeWithTag("Editor.Discard").assertIsDisplayed().performClick()
        rule.waitUntil(5_000) { rule.onAllNodesWithTag("Editor.Text").fetchSemanticsNodes().isEmpty() }
        rule.runOnIdle { assertNull(editor) }
        rule.onNodeWithTag("Homework.Add").performClick()
        rule.onNodeWithTag("Editor.Text").assertIsDisplayed()
        rule.runOnIdle {
            assertNotNull(editor)
            assertNull(editor?.id)
            assertEquals("", editor?.text)
        }
        assertTrue("Unsaved-changes dialog must be closed",
            rule.onAllNodesWithTag("Editor.Discard").fetchSemanticsNodes().isEmpty())
        shot(current.activity, "fullqa-learning-add")
        try {
            rule.onNodeWithTag("Editor.Due", useUnmergedTree = true).performScrollTo().assertIsDisplayed()
        } finally {
            rule.onNodeWithTag("Editor.Cancel").performClick()
            rule.waitUntil(5_000) { rule.onAllNodesWithTag("Editor.Text").fetchSemanticsNodes().isEmpty() }
            rule.runOnIdle { assertNull(editor) }
        }
        assertTrue("Photo must be reachable without scrolling while typing", photoInitiallyVisible)
        assertTrue("Document must be reachable without scrolling while typing", documentInitiallyVisible)
        assertTrue("Photo action did not become visible above the footer after scrolling", photoVisible)
        assertTrue("Document action did not become visible above the footer after scrolling", documentVisible)
    }

    @Test fun settings_catalog_and_pages_dispatch_locally() {
        val current = launchAtScale(1f)
        var settings by mutableStateOf(SettingsUiState(loaded = true, groupName = "А863С",
            groupUpdated = "Обновлено сегодня", theme = ThemeChoice.Light,
            version = "2.0.0", signedIn = false))
        var composedScale = Float.NaN
        current.scenario.onActivity { activity ->
            activity.setContent {
                val actualScale = LocalDensity.current.fontScale
                SideEffect { composedScale = actualScale }
                ZaparaTheme(settings.theme, MotionSettings.Off) {
                    CompositionLocalProvider(LocalShellChrome provides ShellChrome("А863С", false, true) {}) {
                        Surface(modifier = Modifier.fillMaxSize(), color = Zapara.colors.canvas) {
                            SettingsSection(settings, onEvent = { event ->
                                settings = when (event) {
                                    is SettingsEvent.Theme -> settings.copy(theme = ThemeChoice.entries[event.index])
                                    is SettingsEvent.Notify -> settings.copy(notifyEnabled = event.enabled)
                                    else -> settings
                                }
                            }, updates = UpdateUiState(), onChangeGroup = {})
                        }
                    }
                }
            }
        }
        rule.waitForIdle()
        rule.runOnIdle { assertEquals(1f, composedScale) }
        rule.onNodeWithTag("Settings.Overview.Appearance").assertIsDisplayed()
        shot(current.activity, "fullqa-settings-catalog")
        rule.onNodeWithTag("Settings.Overview.Appearance").performClick()
        rule.onNodeWithTag("Settings.Theme.2").assertIsDisplayed().performClick()
        rule.runOnIdle { assertEquals(ThemeChoice.Dark, settings.theme) }
        shot(current.activity, "fullqa-settings-appearance-dark")
        rule.onNodeWithTag("Settings.Overview.Back").performClick()
        rule.onNodeWithTag("Settings.Overview.Notifications").performClick()
        rule.onNodeWithTag("Settings.Notify").assertIsDisplayed().performClick()
        rule.runOnIdle { assertEquals(false, settings.notifyEnabled) }
        shot(current.activity, "fullqa-settings-notifications")
        rule.onNodeWithTag("Settings.Overview.Back").performClick()
        rule.onNodeWithTag("Settings.Overview.Data").performScrollTo().performClick()
        shot(current.activity, "fullqa-settings-local-data")
    }

    @Test fun settings_large_font_keeps_catalog_and_appearance_reachable() {
        val current = launchAtScale(2f)
        current.scenario.onActivity { activity ->
            activity.setContent {
                ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
                    CompositionLocalProvider(LocalShellChrome provides ShellChrome("А863С", false, true) {}) {
                        Surface(modifier = Modifier.fillMaxSize(), color = Zapara.colors.canvas) {
                            SettingsSection(SettingsUiState(loaded = true, groupName = "А863С",
                                theme = ThemeChoice.Light, version = "2.0.0"), {}, UpdateUiState(), {})
                        }
                    }
                }
            }
        }
        rule.onNodeWithTag("Settings.Overview.Appearance").performScrollTo().assertIsDisplayed()
        shot(current.activity, "fullqa-settings-catalog-200")
        rule.onNodeWithTag("Settings.Overview.Appearance").performClick()
        rule.onNodeWithTag("Settings.Theme.1").assertIsDisplayed()
        shot(current.activity, "fullqa-settings-appearance-200")
    }
}
