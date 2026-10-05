package ru.bgtu_voenmeh.zapara.groups

import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.performClick
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.Density
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import ru.bgtu_voenmeh.zapara.ui.shell.Section
import ru.bgtu_voenmeh.zapara.ui.shell.ZBottomBar
import ru.bgtu_voenmeh.zapara.ui.theme.MotionSettings
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme

@RunWith(AndroidJUnit4::class)
class ChatNavigationTest {
    @get:Rule val rule = createComposeRule()

    @Test
    fun chat_has_its_own_primary_button_that_opens_the_unified_inbox() {
        var selected: Section? = null
        rule.setContent {
            ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
                ZBottomBar(Section.Schedule, false, 0, false, { selected = it }, {})
            }
        }
        rule.onNodeWithTag("Nav.Chat").assertIsDisplayed().performClick()
        rule.runOnIdle { assertEquals(Section.Chat, selected) }
        rule.onNodeWithTag("Nav.Sections").assertIsDisplayed()
    }

    @Test
    fun chat_and_sections_remain_reachable_with_large_system_text() {
        rule.setContent {
            ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
                val density = LocalDensity.current.density
                CompositionLocalProvider(LocalDensity provides Density(density, 2f)) {
                    ZBottomBar(Section.Group, false, 0, false, {}, {})
                }
            }
        }
        rule.onNodeWithTag("Nav.Chat").assertIsDisplayed()
        rule.onNodeWithTag("Nav.Sections").assertIsDisplayed()
    }
}
