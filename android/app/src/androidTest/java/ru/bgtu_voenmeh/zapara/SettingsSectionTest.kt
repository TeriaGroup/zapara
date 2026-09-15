package ru.bgtu_voenmeh.zapara

import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.ExperimentalComposeUiApi
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.testTagsAsResourceId
import androidx.test.core.app.ActivityScenario
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.Until
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.settings.SettingsEvent
import ru.bgtu_voenmeh.zapara.ui.settings.SettingsSection
import ru.bgtu_voenmeh.zapara.ui.settings.SettingsUiState
import ru.bgtu_voenmeh.zapara.ui.settings.UpdateUiState
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ShellChrome
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

@OptIn(ExperimentalComposeUiApi::class)
class SettingsSectionTest {
    @Test fun theme_segment_and_about() {
        val events = CopyOnWriteArrayList<SettingsEvent>()
        val state = SettingsUiState(true, "А863С", "Обновлено 08.09 12:00 · сегодня", false, false, ThemeChoice.System, true, version = "2.0.0", selfUpdate = true)
        val ins = InstrumentationRegistry.getInstrumentation()
        val ready = CountDownLatch(1)
        ActivityScenario.launch(ComponentActivity::class.java).use { scenario ->
            lateinit var activity: ComponentActivity
            scenario.onActivity { host ->
                activity = host
                host.setContent {
                    ZaparaTheme(ThemeChoice.Dark) {
                        CompositionLocalProvider(LocalShellChrome provides ShellChrome("А863С · нечёт.", false, true) {}) {
                            Box(Modifier.fillMaxSize().semantics { testTagsAsResourceId = true }) {
                                SettingsSection(state, { events += it }, UpdateUiState(), {})
                            }
                        }
                    }
                }
                ready.countDown()
            }
            assertTrue(ready.await(10, TimeUnit.SECONDS))
            ins.waitForIdleSync()
            val device = UiDevice.getInstance(ins)
            val pkg = installedPackage
            assertTrue(device.wait(Until.hasObject(By.res("Settings.Theme.1").pkg(pkg)), 10_000))
            val theme = requireNotNull(device.findObject(By.res("Settings.Theme.1").pkg(pkg)))
            val visible = theme.visibleBounds
            if (visible.height() <= 0 || visible.bottom > device.displayHeight - 80) {
                device.swipe(device.displayWidth / 2, device.displayHeight * 3 / 4, device.displayWidth / 2, device.displayHeight / 4, 20)
                device.waitForIdle(1000)
            }
            requireNotNull(device.findObject(By.res("Settings.Theme.1").pkg(pkg))).click()
            device.waitForIdle(2000)
            assertTrue(events.any { it is SettingsEvent.Theme && it.index == 1 })
            device.wait(Until.hasObject(By.textContains("Неофициальное")), 3_000)
            Frames.capture(activity, "settings-dark")
        }
        val ready2 = CountDownLatch(1)
        ActivityScenario.launch(ComponentActivity::class.java).use { scenario ->
            lateinit var activity: ComponentActivity
            scenario.onActivity { host ->
                activity = host
                host.setContent {
                    ZaparaTheme(ThemeChoice.Light) {
                        CompositionLocalProvider(LocalShellChrome provides ShellChrome("А863С · нечёт.", false, true) {}) {
                            Box(Modifier.fillMaxSize().semantics { testTagsAsResourceId = true }) {
                                SettingsSection(state.copy(theme = ThemeChoice.Light), {}, UpdateUiState(), {})
                            }
                        }
                    }
                }
                ready2.countDown()
            }
            assertTrue(ready2.await(10, TimeUnit.SECONDS))
            ins.waitForIdleSync()
            Frames.capture(activity, "settings-light")
            Frames.capture(activity, "settings-about-dark")
        }
    }

    @Test fun rustore_build_points_to_github_without_self_update_controls() {
        val state = SettingsUiState(
            loaded = true, groupName = "А863С", groupUpdated = "Обновлено 08.09 12:00 · сегодня",
            theme = ThemeChoice.Dark, version = "2.1.0", selfUpdate = false
        )
        val ins = InstrumentationRegistry.getInstrumentation()
        val ready = CountDownLatch(1)
        ActivityScenario.launch(ComponentActivity::class.java).use { scenario ->
            scenario.onActivity { host ->
                host.setContent {
                    ZaparaTheme(ThemeChoice.Dark) {
                        CompositionLocalProvider(LocalShellChrome provides ShellChrome("А863С · нечёт.", false, true) {}) {
                            Box(Modifier.fillMaxSize().semantics { testTagsAsResourceId = true }) {
                                SettingsSection(state, {}, UpdateUiState(), {})
                            }
                        }
                    }
                }
                ready.countDown()
            }
            assertTrue(ready.await(10, TimeUnit.SECONDS))
            ins.waitForIdleSync()
            val device = UiDevice.getInstance(ins)
            val pkg = installedPackage
            fun shown(res: String): Boolean = device.hasObject(By.res(res).pkg(pkg))
            if (!device.wait(Until.hasObject(By.res("Settings.RustoreGithub").pkg(pkg)), 3_000)) {
                repeat(4) {
                    if (shown("Settings.RustoreGithub")) return@repeat
                    device.swipe(device.displayWidth / 2, device.displayHeight * 3 / 4, device.displayWidth / 2, device.displayHeight / 4, 20)
                    device.waitForIdle(800)
                }
            }
            assertTrue(device.wait(Until.hasObject(By.res("Settings.RustoreGithub").pkg(pkg)), 5_000))
            assertTrue(shown("Settings.RustoreUpdates"))
            assertTrue(device.hasObject(By.textContains("GitHub").pkg(pkg)))
            assertTrue(device.hasObject(By.textContains("RuStore").pkg(pkg)))
            assertTrue(device.findObjects(By.res("Settings.UpdCheck").pkg(pkg)).isEmpty())
            assertTrue(device.findObjects(By.res("Settings.UpdDownload").pkg(pkg)).isEmpty())
        }
    }
}
