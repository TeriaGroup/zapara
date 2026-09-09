package ru.bgtu_voenmeh.zapara

import android.os.Build
import android.os.SystemClock
import androidx.lifecycle.ViewModelProvider
import androidx.test.core.app.ActivityScenario
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.Until
import androidx.test.uiautomator.StaleObjectException
import org.junit.rules.ExternalResource
import ru.bgtu_voenmeh.zapara.ui.shell.ShellViewModel

internal val installedPackage: String
    get() = InstrumentationRegistry.getInstrumentation().targetContext.packageName

/** Real accessibility/input driver for API37, without Espresso's removed hidden API. */
class NativeUiRule : ExternalResource() {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val device = UiDevice.getInstance(instrumentation)
    private var scenario: ActivityScenario<MainActivity>? = null

    override fun before() {
        if (Build.VERSION.SDK_INT >= 33) {
            device.executeShellCommand("pm grant $installedPackage android.permission.POST_NOTIFICATIONS")
        }
        scenario = ActivityScenario.launch(MainActivity::class.java)
        val allow = device.wait(
            Until.findObject(By.res("com.android.permissioncontroller:id/permission_allow_button")),
            3000
        )
        allow?.click()
        device.wait(Until.hasObject(By.pkg(installedPackage)), 15000)
        instrumentation.waitForIdleSync()
    }

    override fun after() {
        scenario?.close()
        scenario = null
    }

    fun onActivity(action: (MainActivity) -> Unit) {
        requireNotNull(scenario).onActivity(action)
    }

    fun treeTexts(): List<String> {
        repeat(8) {
            try {
                return device.findObjects(By.pkg(installedPackage)).mapNotNull { it.text }
            } catch (_: StaleObjectException) {
                android.util.Log.d("ZaparaTest", "Accessibility tree changed; retry bounded poll")
                SystemClock.sleep(40)
            }
        }
        return emptyList()
    }

    fun waitUntil(timeoutMillis: Long, condition: () -> Boolean) {
        val deadline = SystemClock.uptimeMillis() + timeoutMillis
        while (!condition()) {
            check(SystemClock.uptimeMillis() < deadline) { "UI condition timed out; visible text: ${treeTexts()}" }
            SystemClock.sleep(50)
        }
    }

    fun clickText(text: String) {
        val target = requireNotNull(device.wait(Until.findObject(By.text(text).pkg(installedPackage)), 20000)) {
            "Missing clickable text: $text"
        }
        clickVisible(target)
    }

    fun clickRes(res: String) {
        val target = requireNotNull(device.wait(Until.findObject(By.res(res).pkg(installedPackage)), 20000)) {
            "Missing resource: $res"
        }
        clickVisible(target)
    }

    private fun clickVisible(target: androidx.test.uiautomator.UiObject2) {
        val bounds = target.visibleBounds
        check(bounds.width() > 0 && bounds.height() > 0) { "Zero-size click target: $bounds" }
        device.click(bounds.centerX(), bounds.centerY())
        instrumentation.waitForIdleSync()
    }

    fun pressBack() {
        device.pressBack()
        instrumentation.waitForIdleSync()
    }

    fun reloadGuest() {
        onActivity {
            val app = it.application as ZaparaApplication
            app.container.notifyDataChanged()
            shellViewModel(it).refresh()
        }
        instrumentation.waitForIdleSync()
    }

    fun shellViewModel(activity: MainActivity): ShellViewModel {
        val app = activity.application as ZaparaApplication
        return ViewModelProvider(app.host, ShellViewModel.factory(app.container))[ShellViewModel::class.java]
    }

    fun longClickRes(res: String) {
        val target = requireNotNull(device.wait(Until.findObject(By.res(res).pkg(installedPackage)), 20000)) {
            "Missing resource: $res"
        }
        val bounds = target.visibleBounds
        check(bounds.width() > 0 && bounds.height() > 0) { "Zero-size long-click target: $bounds" }
        device.swipe(bounds.centerX(), bounds.centerY(), bounds.centerX(), bounds.centerY(), 120)
        instrumentation.waitForIdleSync()
    }
}
