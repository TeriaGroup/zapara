package ru.bgtu_voenmeh.zapara

import androidx.activity.ComponentActivity
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.sp
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.settings.*
import ru.bgtu_voenmeh.zapara.ui.shell.*
import ru.bgtu_voenmeh.zapara.ui.theme.*

/** Real components, isolated local density; no device settings, Room or profile writes. */
class UxWidthRegressionTest {
    @get:Rule val rule = createAndroidComposeRule<ComponentActivity>()
    private var density = 0f

    @Test fun settings_refresh_words_fit_360dp_200() = settingsWords(360)
    @Test fun settings_refresh_words_fit_411dp_200() = settingsWords(411)

    private fun settingsWords(width: Int) {
        show(width, 2f) {
            SettingsSection(SettingsUiState(loaded = true, groupName = "А863С"), {}, UpdateUiState(), {})
        }
        listOf("Settings.GroupChange" to "Изменить", "Settings.Refresh" to "Обновить расписание").forEach { (tag, label) ->
            rule.onNodeWithTag(tag).performScrollTo().assertIsDisplayed()
            target(tag)
            painted(label, tag, 2f, 15)
        }
    }

    @Test fun settings_actions_dispatch_once_and_loading_blocks_refresh() {
        val events = mutableListOf<SettingsEvent>()
        var changes = 0
        var loading by mutableStateOf(false)
        show(360, 2f) {
            SettingsSection(SettingsUiState(loaded = true, groupName = "А863С", refreshing = loading),
                { events += it }, UpdateUiState(), { changes++ })
        }
        rule.onNodeWithTag("Settings.GroupChange").performScrollTo().performClick()
        rule.onNodeWithTag("Settings.Refresh").performScrollTo().assertIsEnabled().performClick()
        rule.runOnIdle {
            assertEquals(1, changes)
            assertEquals(listOf(SettingsEvent.Refresh), events)
            loading = true
        }
        rule.onNodeWithTag("Settings.Refresh").performScrollTo().assertIsNotEnabled()
            .performTouchInput { click() }
        rule.onNodeWithTag("Settings.GroupChange").assertIsEnabled()
        rule.runOnIdle {
            assertEquals(1, changes)
            assertEquals(listOf(SettingsEvent.Refresh), events)
        }
    }

    @Test fun header_long_title_and_group_stack_at_360dp_100() = header(true)
    @Test fun header_short_title_keeps_inline_layout_at_360dp_100() = header(false)

    private fun header(long: Boolean) {
        val title = if (long) "Преподаватели и расписание" else "Карты"
        val caption = if (long) "А863С · нечётная неделя" else "А863С"
        var groups = 0
        var actions = 0
        show(360, 1f) {
            CompositionLocalProvider(LocalShellChrome provides ShellChrome(caption, false, true) { groups++ }) {
                ZTopBar(title) {
                    ZIconButton(R.drawable.ic_plus, "Добавить", { actions++ }, "UxWidth.Action")
                }
            }
        }
        rule.onNodeWithTag("Top.Title", true).assertTextEquals(title).assertIsDisplayed()
        val titleNode = rule.onNodeWithTag("Top.Title", true).fetchSemanticsNode()
        var naturalTitleWidth = 0f
        rule.runOnIdle {
            val snapshot = RenderedTextEvidence.capture(titleNode)
            RenderedTextEvidence.verify(snapshot, 1f)
            assertEquals(22.sp, snapshot.raw.layoutInput.style.fontSize)
            naturalTitleWidth = snapshot.paragraph.maxIntrinsicWidth
        }
        // GroupChip uses the existing caption token; verify actual text, not a substitute label.
        painted(caption, "Top.GroupChip", 1f, 12)
        target("Top.GroupChip")
        target("UxWidth.Action")
        val titleBounds = titleNode.boundsInRoot
        val groupBounds = rule.onNodeWithTag("Top.GroupChip").fetchSemanticsNode().boundsInRoot
        val actionBounds = rule.onNodeWithTag("UxWidth.Action").fetchSemanticsNode().boundsInRoot
        if (long) {
            val hostWidth = rule.onNodeWithTag("UxWidth.Host").fetchSemanticsNode().boundsInRoot.width
            assertTrue("Fixture genuinely exceeds one row at 100%", naturalTitleWidth + groupBounds.width + actionBounds.width > hostWidth)
            assertTrue("Width pressure must put group below full title", groupBounds.top >= titleBounds.bottom - 0.5f)
            assertTrue("Actions must not consume the long title column", actionBounds.top >= titleBounds.bottom - 0.5f)
        } else {
            assertTrue("Short header stays inline", groupBounds.top < titleBounds.bottom && groupBounds.bottom > titleBounds.top)
            assertTrue("Short header action stays inline", actionBounds.top < titleBounds.bottom && actionBounds.bottom > titleBounds.top)
        }
        rule.onNodeWithTag("Top.GroupChip").performClick()
        rule.onNodeWithTag("UxWidth.Action").performClick()
        rule.runOnIdle { assertEquals(1, groups); assertEquals(1, actions) }
    }

    private fun target(tag: String) {
        val node = rule.onNodeWithTag(tag).assertIsDisplayed().fetchSemanticsNode()
        val host = rule.onNodeWithTag("UxWidth.Host").fetchSemanticsNode().boundsInRoot
        val bounds = node.boundsInRoot
        assertTrue("$tag actual width >=48dp", bounds.width + 0.5f >= 48 * density)
        assertTrue("$tag actual height >=48dp", bounds.height + 0.5f >= 48 * density)
        assertTrue("$tag exposed inside host", bounds.left >= host.left - 0.5f && bounds.right <= host.right + 0.5f &&
            bounds.top >= host.top - 0.5f && bounds.bottom <= host.bottom + 0.5f)
    }

    private fun painted(text: String, ancestor: String, scale: Float, size: Int) {
        val node = rule.onNode(hasText(text) and hasAnyAncestor(hasTestTag(ancestor)), useUnmergedTree = true)
            .assertIsDisplayed().fetchSemanticsNode()
        rule.runOnIdle {
            val snapshot = RenderedTextEvidence.capture(node)
            RenderedTextEvidence.verify(snapshot, scale)
            assertEquals("Preserve typography, do not shrink to fit", size.sp, snapshot.raw.layoutInput.style.fontSize)
        }
    }

    private fun show(width: Int, scale: Float, content: @Composable () -> Unit) {
        rule.setContent {
            BoxWithConstraints(Modifier.fillMaxSize()) {
                // Use the actual host pixels, not device dp or a coerced requiredWidth.
                val controlled = Density(constraints.maxWidth.toFloat() / width, scale)
                SideEffect { density = controlled.density }
                CompositionLocalProvider(LocalDensity provides controlled,
                    LocalShellChrome provides ShellChrome(null, false, false) {}) {
                    ZaparaTheme(ThemeChoice.Dark, MotionSettings.Off) {
                        Box(Modifier.fillMaxSize().testTag("UxWidth.Host")) { content() }
                    }
                }
            }
        }
        rule.waitForIdle()
        val bounds = rule.onNodeWithTag("UxWidth.Host").fetchSemanticsNode().boundsInRoot
        assertEquals("Fixture width in local dp", width.toFloat(), bounds.width / density, 0.5f)
    }
}
