package ru.bgtu_voenmeh.zapara

import androidx.activity.compose.setContent
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.UiDevice
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.theme.*

class OwnedTestHostTest {
    private fun cycle(): String {
        lateinit var id: String
        OwnedTestHost.launch().use { host ->
            id = host.activity.hostId
            var clicks = 0
            host.scenario.onActivity { activity ->
                activity.setContent {
                    val density = androidx.compose.ui.platform.LocalDensity.current
                    androidx.compose.runtime.CompositionLocalProvider(
                        androidx.compose.ui.platform.LocalDensity provides androidx.compose.ui.unit.Density(density.density, 2f)
                    ) { ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) { Showcase { clicks++ } } }
                }
            }
            host.awaitForeground()
            val device = UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
            host.await("fresh heading", 5000) { device.hasObject(By.text("Компоненты оформления")) }
            requireNotNull(device.findObject(By.text("Сохранить"))).click()
            host.await("native click", 2000) { clicks == 1 }
            requireNotNull(device.findObject(By.scrollable(true))).scroll(androidx.test.uiautomator.Direction.DOWN, 0.4f)
        }
        return id
    }

    @Test fun two_cycles_destroy_distinct_hosts() { assertNotEquals(cycle(), cycle()) }

    @Test fun body_failure_is_preserved_and_next_host_is_fresh() {
        val expected = AssertionError("intentional body failure")
        lateinit var failed: Api37TestActivity
        try {
            OwnedTestHost.launch().use { host -> failed = host.activity; throw expected }
        } catch (actual: AssertionError) {
            assertSame(expected, actual)
            assertTrue("Teardown failed", actual.suppressed.isEmpty())
        }
        assertEquals(0L, failed.destroyed.count)
        assertNotEquals(failed.hostId, cycle())
    }

    @Test fun readiness_timeout_is_failure_and_cleans_up() {
        lateinit var failed: Api37TestActivity
        try {
            OwnedTestHost.launch().use { host ->
                failed = host.activity
                host.await("intentional missing root", 100) { false }
            }
            fail("Missing root was accepted")
        } catch (expected: IllegalStateException) {
            assertTrue(expected.message.orEmpty().contains("intentional missing root"))
            assertTrue(expected.suppressed.isEmpty())
        }
        assertEquals(0L, failed.destroyed.count)
    }
}
