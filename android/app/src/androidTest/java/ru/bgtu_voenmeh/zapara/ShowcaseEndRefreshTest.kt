package ru.bgtu_voenmeh.zapara

import androidx.activity.compose.setContent
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.Direction
import androidx.test.uiautomator.UiDevice
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.theme.MotionSettings
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme

class ShowcaseEndRefreshTest {
    @Test fun terminal_refresh_retains_last_icon_and_rejects_missing_icon() {
        OwnedTestHost.launch().use { host ->
            host.scenario.onActivity { activity ->
                activity.setContent {
                    val density = androidx.compose.ui.platform.LocalDensity.current
                    androidx.compose.runtime.CompositionLocalProvider(androidx.compose.ui.platform.LocalDensity provides androidx.compose.ui.unit.Density(density.density, 2f)) {
                        ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) { Showcase {} }
                    }
                }
            }
            host.awaitForeground()
            val device = UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
            host.await("showcase scroll region", 5000) { device.hasObject(By.scrollable(true)) }
            requireNotNull(device.findObject(By.scrollable(true))).scroll(Direction.DOWN, 1f)
            assertEquals("Иконка 27", NativeShowcaseDriver.refreshEndIcon(host, device, 27, "theme-end-regression-present").contentDescription)
            val failure = assertThrows(IllegalStateException::class.java) {
                NativeShowcaseDriver.refreshEndIcon(host, device, 28, "theme-end-regression-missing")
            }
            assertTrue(failure.message.orEmpty().contains("Icon 28 absent at end of scroll region"))
        }
    }
}
