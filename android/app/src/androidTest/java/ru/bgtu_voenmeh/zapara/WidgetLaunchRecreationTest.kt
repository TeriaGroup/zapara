package ru.bgtu_voenmeh.zapara

import android.content.Intent
import android.os.Build
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.Until
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class WidgetLaunchRecreationTest {
    @Test fun consumed_widget_launch_does_not_override_restored_map_after_recreation() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val context = instrumentation.targetContext
        val device = UiDevice.getInstance(instrumentation)
        if (Build.VERSION.SDK_INT >= 33) {
            device.executeShellCommand("pm grant ${context.packageName} android.permission.POST_NOTIFICATIONS")
        }
        val widgetIntent = Intent(context, MainActivity::class.java)
            .putExtra(MainActivity.SECTION_EXTRA, "schedule")
            .putExtra(MainActivity.ARGUMENT_EXTRA, "2026-09-23")

        ActivityScenario.launch<MainActivity>(widgetIntent).use { scenario ->
            assertNotNull(device.wait(Until.findObject(By.res("Top.Title").text("Расписание")), 15_000))
            instrumentation.waitForIdleSync()
            scenario.onActivity { activity ->
                assertFalse(activity.intent.hasExtra(MainActivity.SECTION_EXTRA))
                assertFalse(activity.intent.hasExtra(MainActivity.ARGUMENT_EXTRA))
            }

            val maps = requireNotNull(device.wait(Until.findObject(By.res("Nav.Maps")), 15_000))
            maps.click()
            assertNotNull(device.wait(Until.findObject(By.res("Top.Title").text("Карты")), 15_000))

            scenario.recreate()
            assertNotNull(device.wait(Until.findObject(By.res("Top.Title").text("Карты")), 15_000))
        }
    }
}
