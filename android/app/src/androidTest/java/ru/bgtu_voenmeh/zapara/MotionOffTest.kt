package ru.bgtu_voenmeh.zapara

import android.os.SystemClock
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
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
import androidx.compose.ui.platform.ViewRootForTest
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getAllSemanticsNodes
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
import ru.bgtu_voenmeh.zapara.ui.theme.ShineXKey
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeCrossfade
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.appear
import ru.bgtu_voenmeh.zapara.ui.theme.cascadeLaunches
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

@OptIn(ExperimentalComposeUiApi::class)
class MotionOffTest {
    private val pkg get() = installedPackage

    @Test fun appear_is_instant() {
        cascadeLaunches = 0
        val entry = SystemClock.uptimeMillis()
        launch(content = {
            Column {
                repeat(10) { i ->
                    Box(Modifier.size(24.dp).testTag("Appear.$i").appear(i, entry, MotionSettings.Off))
                }
            }
        }) { activity, device ->
            assertTrue(device.wait(Until.hasObject(By.res("Appear.0").pkg(pkg)), 5000))
            assertEquals(0, cascadeLaunches)
            Frames.capture(activity, "motion-off-dark")
        }
    }

    @Test fun indicator_snaps() {
        launch(waitIdle = false, content = {
            var current by remember { mutableStateOf(Section.Schedule) }
            Box(Modifier.fillMaxWidth()) {
                ZBottomBar(current, false, 0, false, { current = it }, {})
            }
        }) { _, device ->
            assertTrue(device.wait(Until.hasObject(By.res("Nav.Indicator").pkg(pkg)), 5000))
            val before = indicatorCenterX(device, "Nav.Indicator")
            val maps = requireNotNull(device.wait(Until.findObject(By.res("Nav.Maps").pkg(pkg)), 5000))
            maps.click()
            val after = indicatorCenterX(device, "Nav.Indicator")
            assertTrue("IndicatorX $before -> $after", after > before)
        }
    }

    @Test fun skeleton_shine_still() {
        launch(content = {
            Skeleton(Modifier.testTag("Skeleton"))
        }) { activity, device ->
            assertTrue(device.wait(Until.hasObject(By.res("Skeleton").pkg(pkg)), 5000))
            val a = semanticFloat(activity, "Skeleton", ShineXKey)
            SystemClock.sleep(300)
            val b = semanticFloat(activity, "Skeleton", ShineXKey)
            assertEquals(a, b, 0.05f)
        }
    }

    @Test fun theme_crossfade_has_no_snapshot() {
        launch(content = {
            var dark by remember { mutableStateOf(true) }
            ThemeCrossfade(key = dark, motion = MotionSettings.Off) {
                Box(Modifier.fillMaxSize().background(Zapara.colors.canvas).testTag("Theme.Root"))
            }
            ZButton("переключить", { dark = !dark }, tag = "Theme.Switch")
        }) { _, device ->
            assertTrue(device.wait(Until.hasObject(By.res("Theme.Root").pkg(pkg)), 5000))
            assertTrue(device.findObject(By.res("Crossfade.Snapshot").pkg(pkg)) == null)
            device.findObject(By.res("Theme.Switch").pkg(pkg))?.click()
            device.waitForIdle(2000)
            assertTrue(device.findObject(By.res("Crossfade.Snapshot").pkg(pkg)) == null)
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
                    ZaparaTheme(ThemeChoice.Dark, MotionSettings.Off) {
                        CompositionLocalProvider(LocalMotion provides MotionSettings.Off) {
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

    private fun indicatorCenterX(device: UiDevice, res: String): Float {
        val bounds = requireNotNull(device.findObject(By.res(res).pkg(pkg))) { "missing $res" }.visibleBounds
        return bounds.centerX().toFloat()
    }

    private fun semanticFloat(activity: ComponentActivity, tag: String, key: androidx.compose.ui.semantics.SemanticsPropertyKey<Float>): Float {
        var value: Float? = null
        InstrumentationRegistry.getInstrumentation().runOnMainSync {
            fun visit(view: android.view.View) {
                if (view is ViewRootForTest) {
                    val node = view.semanticsOwner.getAllSemanticsNodes(mergingEnabled = false).singleOrNull {
                        it.config.contains(SemanticsProperties.TestTag) && it.config[SemanticsProperties.TestTag] == tag
                    }
                    if (node != null) value = node.config[key]
                }
                if (view is android.view.ViewGroup) repeat(view.childCount) { visit(view.getChildAt(it)) }
            }
            visit(activity.window.decorView)
        }
        return requireNotNull(value) { "missing semantics value $key on $tag" }
    }
}
