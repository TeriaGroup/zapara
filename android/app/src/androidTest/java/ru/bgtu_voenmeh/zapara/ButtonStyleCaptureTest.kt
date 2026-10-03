package ru.bgtu_voenmeh.zapara

import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.assertHeightIsAtLeast
import androidx.compose.ui.test.assertWidthIsAtLeast
import androidx.compose.ui.test.junit4.createEmptyComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTouchInput
import androidx.compose.ui.test.click
import androidx.compose.ui.unit.dp
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.UiDevice
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.theme.MotionSettings
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.ZActionButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZDisclosureButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme

class ButtonStyleCaptureTest {
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

    @Test fun busy_existing_action_label_stays_visible_and_cannot_be_clicked() {
        var clicks = 0
        val current = OwnedTestHost.launch().also { host = it }
        current.scenario.onActivity { activity ->
            activity.setContent {
                ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
                    ZButton("Сохранить", { clicks++ }, busy = true, tag = "ButtonStyle.Busy")
                }
            }
        }
        rule.waitForIdle()
        Frames.capture(current.activity, "buttons-style-after-busy")
        rule.onNodeWithText("Сохранить").assertIsDisplayed()
        rule.onNodeWithTag("ButtonStyle.Busy").assertIsNotEnabled()
            .performTouchInput { click() }
        rule.runOnIdle { assertEquals(0, clicks) }
    }

    @Test fun light_100_native_buttons() = captureButtons(ThemeChoice.Light, 1f)
    @Test fun dark_100_native_buttons() = captureButtons(ThemeChoice.Dark, 1f)
    @Test fun light_200_native_buttons() = captureButtons(ThemeChoice.Light, 2f)
    @Test fun dark_200_native_buttons() = captureButtons(ThemeChoice.Dark, 2f)

    private fun captureButtons(theme: ThemeChoice, scale: Float) {
        val device = UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
        originalFontScale = device.executeShellCommand("settings get system font_scale").trim().also {
            require(it.toFloatOrNull() != null)
        }
        FontScaleReadiness.setAndAwait(scale.toString())
        val current = OwnedTestHost.launch().also { host = it }
        FontScaleReadiness.awaitHost(current, scale)
        val primaryClicks = mutableIntStateOf(0)
        var blockedClicks = 0
        var composedScale = Float.NaN
        current.scenario.onActivity { activity ->
            activity.setContent {
                val actualScale = LocalDensity.current.fontScale
                SideEffect { composedScale = actualScale }
                ZaparaTheme(theme, MotionSettings.Off) {
                    var expanded by remember { mutableStateOf(false) }
                    Column(
                        Modifier.fillMaxSize().background(Zapara.colors.canvas)
                            .verticalScroll(rememberScrollState()).padding(Zapara.space.l),
                        verticalArrangement = Arrangement.spacedBy(Zapara.space.s)
                    ) {
                        Text("Кнопки и состояния", style = Zapara.typography.title, color = Zapara.colors.text1)
                        ZDisclosureButton("Подробности", expanded, { expanded = !expanded },
                            tag = "ButtonStyle.Disclosure", leadingIcon = R.drawable.ic_file)
                        if (expanded) Text("Раздел раскрыт", style = Zapara.typography.body, color = Zapara.colors.text1)
                        ZActionButton("Открыть правила", {}, tag = "ButtonStyle.Action",
                            leadingIcon = R.drawable.ic_file)
                        ZButton("Отправить", { primaryClicks.intValue++ },
                            modifier = Modifier.fillMaxWidth(), tag = "ButtonStyle.Primary")
                        Text("Нажатий: ${primaryClicks.intValue}", style = Zapara.typography.caption, color = Zapara.colors.text2)
                        ZButton("Дополнительное действие", {}, modifier = Modifier.fillMaxWidth(),
                            ghost = true, tag = "ButtonStyle.Secondary")
                        ZButton("Отмена", {}, ghost = true, quiet = true, tag = "ButtonStyle.Quiet")
                        ZButton("Сохранить", { blockedClicks++ }, busy = true,
                            modifier = Modifier.fillMaxWidth(), tag = "ButtonStyle.Busy")
                        ZButton("Недоступно", { blockedClicks++ }, enabled = false,
                            modifier = Modifier.fillMaxWidth(), tag = "ButtonStyle.Disabled")
                        ZButton("Удалить запись", {}, destructive = true,
                            leadingIcon = R.drawable.ic_trash, modifier = Modifier.fillMaxWidth(),
                            tag = "ButtonStyle.Destructive")
                        ZButton("Открыть параметры уведомлений для всех разделов", {},
                            modifier = Modifier.fillMaxWidth(), ghost = true, tag = "ButtonStyle.Long")
                    }
                }
            }
        }
        rule.waitForIdle()
        rule.runOnIdle { assertEquals("Real system font scale reached Compose", scale, composedScale) }
        val prefix = "buttons-style-${theme.name.lowercase()}-${(scale * 100).toInt()}"
        listOf("Disclosure", "Action", "Primary", "Secondary", "Quiet", "Busy", "Disabled",
            "Destructive", "Long").forEach { name ->
            rule.onNodeWithTag("ButtonStyle.$name")
                .assertHeightIsAtLeast(48.dp).assertWidthIsAtLeast(48.dp)
        }
        rule.onNodeWithTag("ButtonStyle.Disclosure").assertIsDisplayed()
        assertTrue(Frames.capture(current.activity, "$prefix-collapsed").length() > 1000)
        rule.onNodeWithTag("ButtonStyle.Primary").performClick()
        rule.onNodeWithText("Нажатий: 1").assertIsDisplayed()
        rule.runOnIdle { assertEquals(1, primaryClicks.intValue) }
        assertTrue(Frames.capture(current.activity, "$prefix-clicked").length() > 1000)
        rule.onNodeWithTag("ButtonStyle.Disclosure").performClick()
        rule.onNodeWithText("Раздел раскрыт").assertIsDisplayed()
        assertTrue(Frames.capture(current.activity, "$prefix-expanded").length() > 1000)
        rule.onNodeWithTag("ButtonStyle.Busy").performScrollTo().assertIsNotEnabled()
            .performTouchInput { click() }
        rule.onNodeWithText("Сохранить").assertIsDisplayed()
        rule.onNodeWithTag("ButtonStyle.Disabled").performScrollTo().assertIsNotEnabled()
            .performTouchInput { click() }
        rule.runOnIdle { assertEquals(0, blockedClicks) }
        rule.onNodeWithTag("ButtonStyle.Long").performScrollTo().assertIsDisplayed()
        assertTrue(Frames.capture(current.activity, "$prefix-long").length() > 1000)
    }
}
