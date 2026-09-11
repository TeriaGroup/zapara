package ru.bgtu_voenmeh.zapara

import android.os.SystemClock
import android.util.Log
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.UiDevice

/** SettingsProvider acknowledgement is not ActivityThread configuration acknowledgement. */
internal object FontScaleReadiness {
    fun setAndAwait(value: String) {
        val expected = requireNotNull(value.toFloatOrNull())
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val device = UiDevice.getInstance(instrumentation)
        device.executeShellCommand("settings put system font_scale $value")
        val deadline = SystemClock.elapsedRealtime() + 5000
        var observed = ""
        do {
            val setting = device.executeShellCommand("settings get system font_scale").trim()
            var processScale = Float.NaN
            instrumentation.runOnMainSync {
                processScale = instrumentation.targetContext.applicationContext.resources.configuration.fontScale
            }
            val current = "requested=$value setting=$setting application=$processScale"
            if (current != observed) { Log.i("FontScaleReadiness", current); observed = current }
            if (setting.toFloatOrNull() == expected && processScale == expected) return
            check(SystemClock.elapsedRealtime() < deadline) { "Configuration propagation timeout: $observed" }
            SystemClock.sleep(25)
        } while (true)
    }

    fun awaitHost(host: OwnedTestHost, expected: Float) {
        host.await("Activity Configuration fontScale=$expected", 5000) {
            var ready = false
            host.scenario.onActivity { activity ->
                check(activity === host.activity) { "Configuration recreated the owned host" }
                ready = activity.resources.configuration.fontScale == expected
            }
            ready
        }
        host.awaitForeground()
    }
}
