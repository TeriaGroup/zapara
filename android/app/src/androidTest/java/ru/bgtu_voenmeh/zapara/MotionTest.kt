package ru.bgtu_voenmeh.zapara

import android.os.SystemClock
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.ExperimentalComposeUiApi
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.testTagsAsResourceId
import androidx.compose.ui.unit.dp
import androidx.test.core.app.ActivityScenario
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.Until
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.components.Skeleton
import ru.bgtu_voenmeh.zapara.ui.shell.Section
import ru.bgtu_voenmeh.zapara.ui.shell.ZBottomBar
import ru.bgtu_voenmeh.zapara.ui.theme.LocalMotion
import ru.bgtu_voenmeh.zapara.ui.theme.MotionSettings
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme
import ru.bgtu_voenmeh.zapara.ui.theme.appear
import ru.bgtu_voenmeh.zapara.ui.theme.cascadeLaunches
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

@OptIn(ExperimentalComposeUiApi::class)
class MotionTest {
    private val pkg get() = installedPackage

    @Test fun appear_cascade_first_eight() {
        cascadeLaunches = 0
        launch(waitIdle = false, content = {
            val entry = remember { SystemClock.uptimeMillis() }
            Column {
                repeat(10) { i ->
                    Box(Modifier.size(24.dp).testTag("Appear.$i").appear(i, entry, MotionSettings.On))
                }
            }
        }) { activity, device ->
            assertTrue(device.wait(Until.hasObject(By.res("Appear.8").pkg(pkg)), 2000))
            assertEquals(8, cascadeLaunches)
            Frames.capture(activity, "motion-appear-mid-dark")
            SystemClock.sleep(800)
            assertTrue(device.hasObject(By.res("Appear.9").pkg(pkg)))
        }
    }

    @Test fun indicator_moves_after_maps() {
        val current = AtomicReference(Section.Schedule)
        launch(content = {
            var section by remember { mutableStateOf(current.get()) }
            Box(Modifier.fillMaxWidth()) {
                ZBottomBar(section, false, 0, false, {
                    current.set(it)
                    section = it
                }, {})
            }
        }) { _, device ->
            assertTrue(device.wait(Until.hasObject(By.res("Nav.Indicator").pkg(pkg)), 5000))
            val before = descFloat(device, "Nav.Indicator")
            val maps = requireNotNull(device.wait(Until.findObject(By.res("Nav.Maps").pkg(pkg)), 5000))
            maps.click()
            device.waitForIdle(2000)
            val after = descFloat(device, "Nav.Indicator")
            assertTrue("IndicatorX $before -> $after", after > before)
        }
    }

    @Test fun skeleton_shine_moves() {
        launch(waitIdle = false, content = {
            Skeleton(Modifier.testTag("Skeleton"))
        }) { _, device ->
            assertTrue(device.wait(Until.hasObject(By.res("Skeleton").pkg(pkg)), 2000))
            val a = descFloat(device, "Skeleton")
            SystemClock.sleep(300)
            val b = descFloat(device, "Skeleton")
            assertTrue("shine $a -> $b", a != b)
        }
    }

    private fun launch(
        waitIdle: Boolean = true,
        content: @Composable () -> Unit,
        body: (ComponentActivity, UiDevice) -> Unit
    ) {
        val ins = InstrumentationRegistry.getInstrumentation()
        val composed = CountDownLatch(1)
        ActivityScenario.launch(ComponentActivity::class.java).use { scenario ->
            lateinit var activity: ComponentActivity
            scenario.onActivity { host ->
                activity = host
                host.setContent {
                    ZaparaTheme(ThemeChoice.Dark, MotionSettings.On) {
                        CompositionLocalProvider(LocalMotion provides MotionSettings.On) {
                            Box(Modifier.fillMaxSize().semantics { testTagsAsResourceId = true }) {
                                content()
                            }
                        }
                    }
                    SideEffect { composed.countDown() }
                }
            }
            assertTrue(composed.await(10, TimeUnit.SECONDS))
            if (waitIdle) ins.waitForIdleSync()
            val device = UiDevice.getInstance(ins)
            if (waitIdle) device.waitForIdle(2000)
            body(activity, device)
        }
    }

    private fun descFloat(device: UiDevice, res: String): Float {
        val obj = requireNotNull(device.findObject(By.res(res).pkg(pkg))) { "missing $res" }
        val raw = obj.contentDescription
        return requireNotNull(raw?.replace(',', '.')?.toFloatOrNull()) { "no numeric description on $res ($raw)" }
    }
}
