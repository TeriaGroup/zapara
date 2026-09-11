package ru.bgtu_voenmeh.zapara

import android.util.Log
import androidx.activity.compose.setContent
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.junit4.createEmptyComposeRule
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.UiDevice
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test

/** Alternating transitions exercise both setup and restoration in one process. */
class FontScaleReadinessTest {
    @get:Rule val rule = createEmptyComposeRule()

    @Test fun alternating_configuration_reaches_current_host_and_compose() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val device = UiDevice.getInstance(instrumentation)
        val original = device.executeShellCommand("settings get system font_scale").trim()
        try {
            listOf(1.5f, 1f, 1.5f).forEach { scale ->
                FontScaleReadiness.setAndAwait(scale.toString())
                Log.i("FontScaleReadiness", "immediate setting=${device.executeShellCommand("settings get system font_scale").trim()} application=${instrumentation.targetContext.applicationContext.resources.configuration.fontScale}")
                OwnedTestHost.launch().use { host ->
                    FontScaleReadiness.awaitHost(host, scale)
                    var composed = Float.NaN
                    host.scenario.onActivity { activity ->
                        Log.i("FontScaleReadiness", "launched host=${activity.hostId} activity=${activity.resources.configuration.fontScale}")
                        assertEquals(scale, activity.resources.configuration.fontScale)
                        activity.setContent {
                            val density = LocalDensity.current
                            SideEffect { composed = density.fontScale }
                            ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme {
                                AccessibilityShowcase()
                            }
                        }
                    }
                    rule.waitForIdle()
                    rule.runOnIdle { assertEquals(scale, composed) }
                }
            }
        } finally {
            FontScaleReadiness.setAndAwait(original)
            assertEquals(original, device.executeShellCommand("settings get system font_scale").trim())
        }
    }
}
