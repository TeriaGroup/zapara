package ru.bgtu_voenmeh.zapara

import android.content.Intent
import android.content.Context
import android.content.ContextWrapper
import android.os.SystemClock
import android.util.Log
import android.view.View
import android.view.ViewGroup
import androidx.compose.ui.platform.AbstractComposeView
import androidx.lifecycle.Lifecycle
import androidx.test.core.app.ActivityScenario
import androidx.test.platform.app.InstrumentationRegistry
import java.util.concurrent.TimeUnit
import org.junit.Assert.*

/** One case owns one host. A failed teardown cannot be treated as a successful case. */
internal class OwnedTestHost private constructor(
    val scenario: ActivityScenario<Api37TestActivity>,
    val activity: Api37TestActivity
) : AutoCloseable {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val deadline = SystemClock.elapsedRealtime() + 60000

    fun remainingMillis(): Long = (deadline - SystemClock.elapsedRealtime()).also {
        check(it > 0) { "Owned host deadline exceeded: ${activity.hostId}" }
    }

    fun awaitForeground() = await("current host focus and accessibility root", 5000) {
        var ready = false
        instrumentation.runOnMainSync {
            ready = activity.lifecycle.currentState == Lifecycle.State.RESUMED &&
                activity.window.decorView.isAttachedToWindow && activity.hasWindowFocus()
        }
        val root = instrumentation.uiAutomation.rootInActiveWindow
        ready && root != null && root.window?.title?.toString() == activity.title.toString()
    }

    fun await(label: String, maximum: Long, condition: () -> Boolean) {
        val end = SystemClock.elapsedRealtime() + minOf(maximum, remainingMillis())
        while (!condition()) {
            check(SystemClock.elapsedRealtime() < end) { "Readiness timeout: $label host=${activity.hostId}" }
            SystemClock.sleep(25)
        }
    }

    override fun close() {
        instrumentation.runOnMainSync {
            fun dispose(view: View) {
                if (view is AbstractComposeView) view.disposeComposition()
                if (view is ViewGroup) repeat(view.childCount) { dispose(view.getChildAt(it)) }
            }
            Log.i("Api37Host", "${activity.hostId} FINISH_REQUEST")
            dispose(activity.window.decorView)
            activity.finishAndRemoveTask()
        }
        if (!activity.destroyed.await(8, TimeUnit.SECONDS)) {
            Thread.getAllStackTraces().forEach { (thread, trace) ->
                Log.e("Api37Host", "${activity.hostId} ${thread.name}: ${trace.joinToString("\n")}")
            }
            throw AssertionError("Host not DESTROYED: ${activity.hostId}; batch must stop")
        }
        assertEquals(Lifecycle.State.DESTROYED, scenario.state)
        scenario.close()
        instrumentation.runOnMainSync {
            assertFalse("Owned window survived destroy", activity.window.decorView.isAttachedToWindow)
        }
        Log.i("Api37Host", "${activity.hostId} CLEANUP_VERIFIED")
    }

    companion object {
        fun launch(): OwnedTestHost {
            val instrumentation = InstrumentationRegistry.getInstrumentation()
            val automation = instrumentation.uiAutomation
            automation.serviceInfo = automation.serviceInfo.apply {
                flags = flags or android.accessibilityservice.AccessibilityServiceInfo.FLAG_RETRIEVE_INTERACTIVE_WINDOWS
            }
            val context = instrumentation.targetContext
            val intent = Intent(context, Api37TestActivity::class.java)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_MULTIPLE_TASK)
            val scenario = ActivityScenario.launch<Api37TestActivity>(intent)
            lateinit var activity: Api37TestActivity
            scenario.onActivity { activity = it }
            return OwnedTestHost(scenario, activity)
        }
    }
}

internal fun ownsContext(activity: android.app.Activity, candidate: Context): Boolean {
    var current = candidate
    while (true) {
        if (current === activity) return true
        if (current !is ContextWrapper || current.baseContext === current) return false
        current = current.baseContext
    }
}
