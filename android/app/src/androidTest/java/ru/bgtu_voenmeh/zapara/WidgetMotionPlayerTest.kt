package ru.bgtu_voenmeh.zapara

import android.appwidget.AppWidgetHost
import android.appwidget.AppWidgetManager
import android.content.ComponentName
import android.graphics.Bitmap
import android.graphics.Color
import android.os.Bundle
import android.os.SystemClock
import android.provider.Settings
import android.view.View
import android.widget.ImageView
import android.widget.RemoteViews
import androidx.test.platform.app.InstrumentationRegistry
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.ViewModelStore
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.widgets.*
import ru.bgtu_voenmeh.zapara.ui.settings.SettingsEvent
import ru.bgtu_voenmeh.zapara.ui.settings.SettingsViewModel
import ru.bgtu_voenmeh.zapara.ui.shell.ShellEvent
import ru.bgtu_voenmeh.zapara.ui.shell.ShellViewModel
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

class WidgetMotionPlayerTest {
    private data class Fixture(val host: AppWidgetHost, val id: Int, val view: android.appwidget.AppWidgetHostView)
    companion object {
        private var fixture: Fixture? = null

        @org.junit.AfterClass @JvmStatic fun releaseHost() {
            val retained = fixture ?: return
            val instrumentation = InstrumentationRegistry.getInstrumentation()
            instrumentation.runOnMainSync {
                WidgetMotionPlayer.cancel(retained.id)
                retained.host.stopListening()
            }
            retained.host.deleteAppWidgetId(retained.id)
            instrumentation.uiAutomation.dropShellPermissionIdentity()
            fixture = null
        }
    }
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val context = instrumentation.targetContext
    private val app get() = context.applicationContext as ZaparaApplication
    private val identity get() = WidgetJobIdentity.of(app.host.container.profile, app.host.generation.value)
    private val enabled = WidgetMotionPolicy.of(true, 1f, true)
    private fun main(block: () -> Unit) = instrumentation.runOnMainSync(block)
    private fun finalFace() = RemoteViews(context.packageName, R.layout.widget_schedule).apply {
        setTextViewText(R.id.widget_schedule_title, "Motion final")
    }
    private fun bitmap() = Bitmap.createBitmap(32, 32, Bitmap.Config.ARGB_8888).apply { eraseColor(Color.CYAN) }
    private fun policy(value: WidgetMotionPolicy) =
        WidgetMotionPlayer.updatePolicy(identity, value, WidgetMotionPlayer.beginPolicyObservation())

    private fun withWidget(block: (Int, android.appwidget.AppWidgetHostView) -> Unit) {
        // Deleting a provider's last widget launches onDeleted/onDisabled refreshes asynchronously.
        // Retain one class-owned fixture so those replacement publications cannot race the next case.
        fixture?.let {
            main { WidgetMotionPlayer.clear(); policy(enabled) }
            block(it.id, it.view)
            return
        }
        val initialPublication = CountDownLatch(1)
        val host = object : AppWidgetHost(context, 54105) {
            override fun onCreateView(context: android.content.Context, appWidgetId: Int, info: android.appwidget.AppWidgetProviderInfo) =
                object : android.appwidget.AppWidgetHostView(context) {
                    override fun updateAppWidget(views: RemoteViews?) {
                        super.updateAppWidget(views)
                        val subtitle = findViewById<android.widget.TextView>(R.id.widget_schedule_subtitle)
                        // A profile-clear has an empty subtitle. The real completed face has guest/group text.
                        if (!subtitle?.text.isNullOrBlank()) initialPublication.countDown()
                    }
                }
        }
        val manager = AppWidgetManager.getInstance(context)
        val id = host.allocateAppWidgetId()
        val provider = ComponentName(context, ScheduleWidgetProvider::class.java)
        var view: android.appwidget.AppWidgetHostView? = null
        try {
            instrumentation.uiAutomation.adoptShellPermissionIdentity("android.permission.BIND_APPWIDGET")
            assertTrue(manager.bindAppWidgetIdIfAllowed(id, provider, Bundle()))
            main {
                host.startListening()
                view = host.createView(context, id, manager.getAppWidgetInfo(id))
            }
            assertTrue("Provider must deliver a complete initial face", initialPublication.await(5, TimeUnit.SECONDS))
            main { WidgetMotionPlayer.clear(); policy(enabled) }
            fixture = Fixture(host, id, view!!)
            block(id, view!!)
        } finally {
            if (fixture == null) {
                main { WidgetMotionPlayer.cancel(id); host.stopListening() }
                host.deleteAppWidgetId(id)
                instrumentation.uiAutomation.dropShellPermissionIdentity()
            }
        }
    }

    @Test fun player_requires_prior_same_profile_final_and_disabled_motion_never_allocates() = withWidget { id, _ ->
        // A queued provider can publish after withWidget's reset but before this case runs.
        // Reproduce that ordering without waiting for a timing-dependent broadcast.
        main {
            WidgetMotionPlayer.publishFinal(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss,
                identity, enabled, finalFace())
        }
        main {
            // Reset the first-face precondition in the same main-thread turn as its assertions.
            // Real provider callbacks cannot interleave between this reset and publish/play.
            WidgetMotionPlayer.clear()
            policy(enabled)
            WidgetMotionPlayer.play(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, enabled) { error("no final face") }
            WidgetMotionPlayer.publishFinal(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, enabled, finalFace())
            WidgetMotionPlayer.play(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, enabled) { error("first face") }
            assertFalse(WidgetMotionPlayer.isRunning(id))
            WidgetMotionPlayer.publishFinal(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, enabled, finalFace())
            WidgetMotionPlayer.play(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, WidgetMotionPolicy.Disabled) { error("disabled") }
            assertFalse(WidgetMotionPlayer.isRunning(id))
        }
    }

    @Test fun replacement_cancels_pending_callbacks_and_hides_overlay() = withWidget { id, view ->
        var firstFrames = 0
        main {
            repeat(2) { WidgetMotionPlayer.publishFinal(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, enabled, finalFace()) }
            WidgetMotionPlayer.play(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, enabled) { firstFrames++; bitmap() }
        }
        SystemClock.sleep(100)
        var stoppedAt = 0
        main {
            assertTrue("Scene stopped; host title=${view.findViewById<android.widget.TextView>(R.id.widget_schedule_title).text}", WidgetMotionPlayer.isRunning(id))
            WidgetMotionPlayer.publishFinal(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, enabled, finalFace())
            stoppedAt = firstFrames
            assertFalse(WidgetMotionPlayer.isRunning(id))
        }
        SystemClock.sleep(500)
        main {
            assertEquals(stoppedAt, firstFrames)
            assertEquals(View.GONE, view.findViewById<ImageView>(R.id.widget_schedule_toss).visibility)
        }
    }

    @Test fun allocation_failure_keeps_final_face_and_stale_identity_cannot_draw() = withWidget { id, view ->
        main {
            repeat(2) { WidgetMotionPlayer.publishFinal(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, enabled, finalFace()) }
            WidgetMotionPlayer.play(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity.copy(generation = -1), enabled) { error("stale") }
            WidgetMotionPlayer.play(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, enabled) { throw OutOfMemoryError("injected allocation failure") }
        }
        SystemClock.sleep(600)
        main {
            assertFalse(WidgetMotionPlayer.isRunning(id))
            assertEquals(View.GONE, view.findViewById<ImageView>(R.id.widget_schedule_toss).visibility)
            assertEquals("Motion final", view.findViewById<android.widget.TextView>(R.id.widget_schedule_title).text.toString())
        }
    }

    @Test fun seven_partial_frames_finish_with_hidden_overlay_and_current_accessible_text() = withWidget { id, view ->
        val progress = mutableListOf<Float>()
        main {
            repeat(2) { WidgetMotionPlayer.publishFinal(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, enabled, finalFace()) }
            WidgetMotionPlayer.play(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, enabled) { progress += it; bitmap() }
        }
        SystemClock.sleep(650)
        main {
            assertEquals(7, progress.size)
            assertEquals(0f, progress.first())
            assertEquals(1f, progress.last())
            assertFalse(WidgetMotionPlayer.isRunning(id))
            assertEquals(View.GONE, view.findViewById<ImageView>(R.id.widget_schedule_toss).visibility)
            assertEquals("Motion final", view.findViewById<android.widget.TextView>(R.id.widget_schedule_title).text.toString())
        }
    }

    @Test fun queued_enabled_policy_cannot_restart_after_system_motion_is_disabled() = withWidget { id, _ ->
        val original = Settings.Global.getFloat(context.contentResolver, Settings.Global.ANIMATOR_DURATION_SCALE, 1f)
        var frames = 0
        try {
            instrumentation.uiAutomation.adoptShellPermissionIdentity("android.permission.WRITE_SECURE_SETTINGS", "android.permission.BIND_APPWIDGET")
            Settings.Global.putFloat(context.contentResolver, Settings.Global.ANIMATOR_DURATION_SCALE, 0f)
            SystemClock.sleep(300)
            main {
                repeat(2) { WidgetMotionPlayer.publishFinal(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, enabled, finalFace()) }
                WidgetMotionPlayer.play(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, enabled) { frames++; bitmap() }
            }
            SystemClock.sleep(600)
            main { assertEquals("A queued policy cannot override current system reduced motion", 0, frames) }
        } finally {
            Settings.Global.putFloat(context.contentResolver, Settings.Global.ANIMATOR_DURATION_SCALE, original)
        }
    }

    @Test fun latest_app_policy_cancels_frames_and_rejects_an_older_enabled_publication() = withWidget { id, _ ->
        var frames = 0
        main {
            val oldEnabledRead = WidgetMotionPlayer.beginPolicyObservation()
            val newDisabledRead = WidgetMotionPlayer.beginPolicyObservation()
            repeat(2) { WidgetMotionPlayer.publishFinal(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, enabled, finalFace()) }
            WidgetMotionPlayer.play(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, enabled) { frames++; bitmap() }
            WidgetMotionPlayer.updatePolicy(identity, WidgetMotionPolicy.Disabled, newDisabledRead)
            assertFalse(WidgetMotionPlayer.isRunning(id))
            // Deliver the older IO result after the new disabled result, for the same profile.
            WidgetMotionPlayer.updatePolicy(identity, enabled, oldEnabledRead)
            WidgetMotionPlayer.publishFinal(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, enabled, finalFace())
            WidgetMotionPlayer.play(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, enabled) { frames++; bitmap() }
        }
        SystemClock.sleep(550)
        main {
            assertEquals(0, frames)
            policy(enabled)
        }
    }

    @Test fun settings_model_stops_motion_before_saving_and_refreshes_persisted_policy() =
        preferenceSignal(settings = true)

    @Test fun shell_model_stops_motion_before_saving_and_refreshes_persisted_policy() =
        preferenceSignal(settings = false)

    private fun preferenceSignal(settings: Boolean) = withWidget { id, view ->
        val container = app.host.container
        val original = container.repo.settings().animations
        val models = ViewModelStore()
        lateinit var change: (Boolean) -> Unit
        main {
            if (settings) {
                val model = ViewModelProvider(models, SettingsViewModel.factory(container))[SettingsViewModel::class.java]
                change = { model.onEvent(SettingsEvent.Animations(it)) }
            } else {
                val model = ViewModelProvider(models, ShellViewModel.factory(container))[ShellViewModel::class.java]
                change = { model.onEvent(ShellEvent.Animations(it)) }
            }
        }
        fun awaitSaved(value: Boolean) {
            val deadline = SystemClock.uptimeMillis() + 5_000
            while (container.repo.settings().animations != value && SystemClock.uptimeMillis() < deadline) SystemClock.sleep(10)
            assertEquals(value, container.repo.settings().animations)
            instrumentation.waitForIdleSync()
        }
        fun awaitRefreshed() {
            val deadline = SystemClock.uptimeMillis() + 5_000
            var refreshed = false
            do {
                main { refreshed = view.findViewById<android.widget.TextView>(R.id.widget_schedule_title).text.toString() == context.getString(R.string.nav_schedule) }
                if (!refreshed) SystemClock.sleep(10)
            } while (!refreshed && SystemClock.uptimeMillis() < deadline)
            assertTrue("A completed save must refresh the persisted widget state", refreshed)
        }
        try {
            if (!original) {
                main {
                    WidgetMotionPlayer.publishFinal(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, enabled, finalFace())
                    change(true)
                }
                awaitSaved(true)
                awaitRefreshed()
            }
            // Hold the real settings transaction: cancellation must not wait for disk persistence.
            container.db.runInTransaction {
                main {
                    policy(enabled)
                    repeat(2) { WidgetMotionPlayer.publishFinal(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, enabled, finalFace()) }
                    WidgetMotionPlayer.play(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, enabled) { bitmap() }
                    assertTrue(WidgetMotionPlayer.isRunning(id))
                    change(false)
                    assertFalse("The real Animations event must synchronously cancel before its database save", WidgetMotionPlayer.isRunning(id))
                }
            }
            awaitSaved(false)
            awaitRefreshed()
            main {
                repeat(2) { WidgetMotionPlayer.publishFinal(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, enabled, finalFace()) }
                WidgetMotionPlayer.play(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, enabled) { error("persisted off must block stale motion") }
                assertFalse(WidgetMotionPlayer.isRunning(id))
            }
        } finally {
            main {
                WidgetMotionPlayer.publishFinal(context, id, R.layout.widget_schedule, R.id.widget_schedule_toss, identity, WidgetMotionPolicy.Disabled, finalFace())
                change(original)
            }
            awaitSaved(original)
            awaitRefreshed()
            main { models.clear() }
        }
    }
}
