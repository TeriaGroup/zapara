package ru.bgtu_voenmeh.zapara

import android.provider.Settings
import android.graphics.BitmapFactory
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.runtime.*
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.sp
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.text.font.FontWeight
import androidx.test.core.app.ActivityScenario
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.By
import androidx.test.uiautomator.Until
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.theme.*
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference
import java.util.concurrent.atomic.AtomicBoolean

/** Shared real-platform driver and additional system-setting verification on API37. */
class ThemeNativeCaptureTest {
    @Test fun dark_pixels() = capture(ThemeChoice.Dark, 1f)
    @Test fun light_pixels() = capture(ThemeChoice.Light, 1f)
    @Test fun dark_large_pixels() = capture(ThemeChoice.Dark, 1.3f)
    @Test fun light_large_pixels() = capture(ThemeChoice.Light, 1.3f)

    @Test fun system_choice_follows_device_night_mode() {
        val device = UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
        val original = device.executeShellCommand("cmd uimode night").trim().substringAfter("Night mode: ")
        check(original in setOf("yes", "no", "auto", "custom")) { "Unknown night mode: $original" }
        try {
            device.executeShellCommand("cmd uimode night yes")
            capture(ThemeChoice.System, 1f, DarkColors)
            device.executeShellCommand("cmd uimode night no")
            capture(ThemeChoice.System, 1f, LightColors)
        } finally {
            device.executeShellCommand("cmd uimode night $original")
            assertEquals("Night mode: $original", device.executeShellCommand("cmd uimode night").trim())
        }
    }

    internal fun capture(choice: ThemeChoice, scale: Float, expected: ZaparaColors = if (choice == ThemeChoice.Dark) DarkColors else LightColors) {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val composed = CountDownLatch(1)
        val palette = AtomicReference<ZaparaColors>()
        val clicked = AtomicBoolean(false)
        ActivityScenario.launch(ComponentActivity::class.java).use { scenario ->
            lateinit var activity: ComponentActivity
            scenario.onActivity { host ->
                activity = host
                host.setContent {
                    val density = LocalDensity.current
                    CompositionLocalProvider(LocalDensity provides Density(density.density, scale)) {
                        ZaparaTheme(choice, MotionSettings.Off) {
                            val c = Zapara.colors
                            assertEquals(Inter, Zapara.typography.body.fontFamily)
                            assertEquals(22.sp, Zapara.typography.title.fontSize)
                            assertEquals(FontWeight.Medium, Zapara.typography.section.fontWeight)
                            assertEquals(19.5.sp, Zapara.typography.body.lineHeight)
                            assertFalse(Zapara.motion.enabled)
                            Showcase { clicked.set(true) }
                            SideEffect { palette.set(c); composed.countDown() }
                        }
                    }
                }
            }
            assertTrue("Composition did not complete", composed.await(10, TimeUnit.SECONDS))
            instrumentation.waitForIdleSync()
            val device = UiDevice.getInstance(instrumentation)
            device.waitForIdle(2000)
            assertEquals(expected, palette.get())
            assertNotNull(device.findObject(By.text("Компоненты оформления")))
            for (i in 1..27) {
                val icon = requireNotNull(device.findObject(By.desc("Иконка $i"))) { "Icon $i not visible" }
                assertFalse(icon.visibleBounds.isEmpty)
            }
            val button = requireNotNull(device.wait(Until.findObject(By.text("Сохранить")), 5000))
            // Accessibility may expose the label node; clickable ancestor is the actual hit target.
            var target = button
            while (!target.isClickable && target.parent != null) target = target.parent
            assertTrue(target.isClickable)
            val density = activity.resources.displayMetrics.density
            assertTrue(target.visibleBounds.width() / density >= 44f)
            assertTrue(target.visibleBounds.height() / density >= 44f)
            val buttonBounds = target.visibleBounds
            val rootLocation = IntArray(2)
            instrumentation.runOnMainSync {
                activity.findViewById<android.view.View>(android.R.id.content).getLocationOnScreen(rootLocation)
            }
            target.click()
            instrumentation.waitForIdleSync()
            assertTrue("Native input did not invoke onClick", clicked.get())
            val suffix = if (expected.isDark) "dark" else "light"
            val variant = if (choice == ThemeChoice.System) "system-" else if (scale > 1) "large-" else ""
            val pressed = Frames.capture(activity, "theme-native-$variant$suffix-pressed")
            val pressedBitmap = BitmapFactory.decodeFile(pressed.path)
            assertEquals("Motion Off must not start a ripple", expected.accent.toArgb(), pressedBitmap.getPixel(
                buttonBounds.centerX() - rootLocation[0], buttonBounds.top + (4 * density).toInt() - rootLocation[1]))
            pressedBitmap.recycle()
            device.waitForIdle(2000)
            var disabled = requireNotNull(device.findObject(By.text("Недоступно")))
            while (disabled.isEnabled && disabled.parent != null) disabled = disabled.parent
            assertFalse(disabled.isEnabled)
            val file = Frames.capture(activity, "theme-native-$variant$suffix")
            assertTrue(file.length() > 1000)
            val bitmap = BitmapFactory.decodeFile(file.path)
            assertEquals(expected.canvas.toArgb(), bitmap.getPixel(0, 0))
            bitmap.recycle()
        }
    }

    @Test fun observes_system_motion_changes_and_app_preference() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val resolver = instrumentation.targetContext.contentResolver
        val original = Settings.Global.getString(resolver, Settings.Global.ANIMATOR_DURATION_SCALE)
        val device = UiDevice.getInstance(instrumentation)
        val current = AtomicReference<MotionSettings>()
        val changed = AtomicReference(CountDownLatch(1))
        val appSwitch = mutableStateOf(true)
        ActivityScenario.launch(ComponentActivity::class.java).use { scenario ->
            try {
                device.executeShellCommand("settings put global animator_duration_scale 1")
                scenario.onActivity { host ->
                    host.setContent {
                        ZaparaTheme(ThemeChoice.Dark, MotionSettings(appSwitch.value, 1f)) {
                            val motion = Zapara.motion
                            SideEffect { current.set(motion); changed.get().countDown() }
                        }
                    }
                }
                assertTrue(changed.get().await(10, TimeUnit.SECONDS))
                assertTrue(current.get().enabled)
                changed.set(CountDownLatch(1))
                device.executeShellCommand("settings put global animator_duration_scale 0")
                assertTrue(changed.get().await(10, TimeUnit.SECONDS))
                assertFalse(current.get().enabled)
                assertEquals(0, current.get().ms(Durations.theme))
                changed.set(CountDownLatch(1))
                device.executeShellCommand("settings put global animator_duration_scale 1")
                assertTrue(changed.get().await(10, TimeUnit.SECONDS))
                assertTrue(current.get().enabled)
                changed.set(CountDownLatch(1))
                instrumentation.runOnMainSync { appSwitch.value = false }
                assertTrue(changed.get().await(10, TimeUnit.SECONDS))
                assertFalse(current.get().enabled)
            } finally {
                device.executeShellCommand(if (original == null) "settings delete global animator_duration_scale"
                    else "settings put global animator_duration_scale $original")
                assertEquals(original, Settings.Global.getString(resolver, Settings.Global.ANIMATOR_DURATION_SCALE))
                runCatching { scenario.onActivity { it.finish() } }
            }
        }
    }
}
