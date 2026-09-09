@file:OptIn(androidx.compose.ui.ExperimentalComposeUiApi::class)

package ru.bgtu_voenmeh.zapara

import android.view.KeyEvent
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.input.InputModeManager
import androidx.compose.ui.input.key.onPreviewKeyEvent
import androidx.compose.ui.platform.LocalInputModeManager
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.testTagsAsResourceId
import androidx.test.core.app.ActivityScenario
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.*
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.theme.*
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

class ShellFocusTest {
    @Test fun dark_off() = verifyFocus(ThemeChoice.Dark, MotionSettings.Off)
    @Test fun dark_on() = verifyFocus(ThemeChoice.Dark, MotionSettings.On)
    @Test fun light_off() = verifyFocus(ThemeChoice.Light, MotionSettings.Off)
    @Test fun light_on() = verifyFocus(ThemeChoice.Light, MotionSettings.On)

    private fun verifyFocus(choice: ThemeChoice, motion: MotionSettings) {
        val ins = InstrumentationRegistry.getInstrumentation()
        val device = UiDevice.getInstance(ins)
        var disabledClicks = 0
                ActivityScenario.launch(ComponentActivity::class.java).use { scenario ->
                    val composed = CountDownLatch(1)
                    val ghostRequester = FocusRequester()
                    lateinit var activity: ComponentActivity
                    lateinit var inputMode: InputModeManager
                    scenario.onActivity { host ->
                        activity = host
                        host.setContent {
                            val manager = LocalInputModeManager.current
                            ZaparaTheme(choice, motion) {
                                val actualMotion = Zapara.motion.enabled
                                SideEffect {
                                    inputMode = manager
                                    ShellFocusDiagnostics.log("composition mode=${manager.inputMode} requestedMotion=${motion.enabled} actualMotion=$actualMotion")
                                    composed.countDown()
                                }
                                Column(Modifier.fillMaxSize().background(Zapara.colors.canvas)
                                    .semantics { testTagsAsResourceId = true }
                                    .onPreviewKeyEvent {
                                        val event = it.nativeKeyEvent
                                        ShellFocusDiagnostics.log("KEY action=${event.action} code=${event.keyCode} meta=${event.metaState} device=${event.deviceId} source=${event.source} mode=${manager.inputMode}")
                                        false
                                    }.padding(Zapara.space.l)) {
                                    ZButton("Сохранить", {}, Modifier.testTag("Focus.Primary")
                                        .onFocusChanged { ShellFocusDiagnostics.log("COMPOSE Primary=$it") })
                                    Spacer(Modifier.height(Zapara.space.l))
                                    ZButton("Отмена", {}, Modifier.testTag("Focus.Ghost")
                                        .focusRequester(ghostRequester)
                                        .onFocusChanged { ShellFocusDiagnostics.log("COMPOSE Ghost=$it") }, ghost = true)
                                    Spacer(Modifier.height(Zapara.space.l))
                                    ZButton("Недоступно", { disabledClicks++ }, Modifier.testTag("Focus.Disabled"), enabled = false)
                                }
                            }
                        }
                    }
                    assertTrue("Composition did not complete", composed.await(10, TimeUnit.SECONDS))
                    // Native test precondition, not a focus assignment; a previous pointer test may leave Touch mode.
                    ins.setInTouchMode(false)
                    ins.waitForIdleSync()
                    val suffix = "${choice.key}-${if (motion.enabled) "on" else "off"}"
                    val diagnostic = ShellFocusDiagnostics(activity, inputMode)
                    diagnostic.trace("initial-$suffix")
                    diagnostic.capture("initial-$suffix")
                    assertTrue(device.wait(Until.hasObject(By.text("Сохранить").pkg(activity.packageName)), 10_000))
                    assertNotNull(device.findObject(By.text("Сохранить").pkg(activity.packageName)))
                    val primary = requireNotNull(device.findObject(By.res("Focus.Primary").pkg(activity.packageName)))
                    assertTrue("Primary must be the actual clickable ancestor", primary.isClickable)
                    val bounds = primary.visibleBounds
                    val ghost = requireNotNull(device.findObject(By.res("Focus.Ghost").pkg(activity.packageName)))
                    val ghostBounds = ghost.visibleBounds
                    val disabled = requireNotNull(device.findObject(By.res("Focus.Disabled").pkg(activity.packageName)))
                    assertFalse("Disabled must not be focusable", disabled.isFocusable)
                    assertFalse("Disabled must not be enabled", disabled.isEnabled)
                    val disabledBounds = disabled.visibleBounds
                    for (rect in listOf(bounds, ghostBounds, disabledBounds)) {
                        val minimum = 44 * activity.resources.displayMetrics.density
                        assertTrue("Minimum 44dp touch area", rect.width() >= minimum && rect.height() >= minimum)
                    }
                    // ARRANGE only: this does not count as keyboard input or a successful transition.
                    ins.runOnMainSync { ghostRequester.requestFocus() }
                    val arranged = device.wait(Until.hasObject(By.res("Focus.Ghost").pkg(activity.packageName).focused(true)), 5000)
                    diagnostic.trace("arranged-ghost-$suffix")
                    val rest = diagnostic.capture("rest-$suffix")
                    assertTrue("ARRANGE Ghost must own actual focus before native key", arranged)
                    assertFalse(device.hasObject(By.res("Focus.Primary").pkg(activity.packageName).focused(true)))

                    val injected = device.pressKeyCode(KeyEvent.KEYCODE_TAB, KeyEvent.META_SHIFT_ON)
                    assertTrue("Native Shift+Tab injection", injected)
                    ShellFocusDiagnostics.log("SHIFT_TAB injected=$injected")
                    val focused = device.wait(Until.hasObject(By.res("Focus.Primary").pkg(activity.packageName).focused(true)), 5000)
                    diagnostic.trace("shift-tab-$suffix")
                    val focusedFrame = diagnostic.capture("primary-$suffix")
                    val colors = if (choice == ThemeChoice.Dark) DarkColors else LightColors
                    val primaryOutline = diagnostic.compare(rest, focusedFrame, bounds, "primary-$suffix", colors.onAccent.toArgb())
                    assertTrue("Native Shift+Tab must focus actual Primary: $suffix", focused)

                    val inverseInjected = device.pressKeyCode(KeyEvent.KEYCODE_TAB)
                    assertTrue("Native Tab injection", inverseInjected)
                    val inverse = device.wait(Until.hasObject(By.res("Focus.Ghost").pkg(activity.packageName).focused(true)), 5000)
                    ShellFocusDiagnostics.log("TAB inverseInjected=$inverseInjected inverseGhost=$inverse")
                    diagnostic.trace("inverse-tab-$suffix")
                    val ghostFrame = diagnostic.capture("inverse-ghost-$suffix")
                    val ghostOutline = diagnostic.compare(focusedFrame, ghostFrame, ghostBounds, "ghost-$suffix", colors.text1.toArgb())
                    assertTrue("Native inverse Tab must focus actual Ghost", inverse)
                    assertEquals("Primary geometry must not shift", bounds, device.findObject(By.res("Focus.Primary").pkg(activity.packageName)).visibleBounds)
                    assertEquals("Ghost geometry must not shift", ghostBounds, device.findObject(By.res("Focus.Ghost").pkg(activity.packageName)).visibleBounds)
                    assertFalse("Disabled must never acquire focus", device.hasObject(By.res("Focus.Disabled").pkg(activity.packageName).focused(true)))
                    device.click(disabledBounds.centerX(), disabledBounds.centerY())
                    ins.waitForIdleSync()
                    assertEquals("Disabled click must not invoke callback", 0, disabledClicks)
                    assertTrue("Contrasting straight inset outline required: $suffix primary=$primaryOutline ghost=$ghostOutline (corners/ripple cannot satisfy)", primaryOutline && ghostOutline)
                }
    }
}
