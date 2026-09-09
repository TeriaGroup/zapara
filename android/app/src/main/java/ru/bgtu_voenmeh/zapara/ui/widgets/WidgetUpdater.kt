package ru.bgtu_voenmeh.zapara.ui.widgets

import android.content.Context
import android.content.res.Configuration
import android.os.Handler
import android.os.Looper
import android.util.Log
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeoutOrNull
import ru.bgtu_voenmeh.zapara.ZaparaApplication

object WidgetUpdater {
    private val gate = Any()
    private var bound = false
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val main = Handler(Looper.getMainLooper())
    private val restoreLock = Mutex()

    fun bindIfNeeded(context: Context) {
        val app = context.applicationContext as? ZaparaApplication ?: return
        bind(app)
    }

    fun bind(app: ZaparaApplication) {
        synchronized(gate) {
            if (bound) return
            bound = true
        }
        scope.launch {
            restoreIfGuest(app)
            app.host.generation.collectLatest { gen ->
                push(app, gen, clearFirst = true)
                app.host.container.events.events.collect {
                    push(app, app.host.generation.value, clearFirst = false)
                }
            }
        }
    }

    fun refresh(context: Context) {
        val app = context.applicationContext as? ZaparaApplication ?: return
        bind(app)
        scope.launch {
            restoreIfGuest(app)
            push(app, app.host.generation.value, clearFirst = false)
        }
    }

    private suspend fun restoreIfGuest(app: ZaparaApplication) {
        restoreLock.withLock {
            WidgetVaultRestore.beforePush(app.host.container.profile.isGuest) {
                try {
                    val done = withTimeoutOrNull(10_000) { app.host.restore() }
                    if (done == null) Log.w("ZaparaWidget", "restore timeout")
                } catch (e: Exception) {
                    Log.w("ZaparaWidget", "restore", e)
                }
            }
        }
    }

    private fun push(app: ZaparaApplication, gen: Long, clearFirst: Boolean) {
        val container = app.host.container
        if (container.closed) return
        val identity = WidgetJobIdentity.of(container.profile, gen)
        val night = night(app)
        if (clearFirst) {
            val copy = container.copy
            val dark = WidgetTheme.isDark("system", night)
            apply(
                app,
                ScheduleWidgetComposer.cleared(identity, copy, dark),
                HomeworkWidgetComposer.cleared(identity, copy, dark)
            )
        }
        container.work.enter().use { ticket ->
            if (!ticket.admitted) return
            val schedule = try {
                WidgetSnapshots.schedule(container, identity, false, night)
            } catch (e: Exception) {
                Log.w("ZaparaWidget", "schedule", e)
                return
            }
            val homework = try {
                WidgetSnapshots.homework(container, identity, false, night)
            } catch (e: Exception) {
                Log.w("ZaparaWidget", "homework", e)
                return
            }
            val current = WidgetJobIdentity.of(app.host.container.profile, app.host.generation.value)
            if (!WidgetJobs.canApply(ticket, identity, current)) return
            apply(app, schedule, homework)
        }
    }

    private fun apply(
        app: ZaparaApplication,
        schedule: ScheduleWidgetSnapshot,
        homework: HomeworkWidgetSnapshot
    ) {
        main.post {
            val current = WidgetJobIdentity.of(app.host.container.profile, app.host.generation.value)
            if (!WidgetJobs.accept(schedule.identity, current)) return@post
            if (!WidgetJobs.accept(homework.identity, current)) return@post
            try {
                WidgetRemoteViews.pushSchedule(app, schedule)
                WidgetRemoteViews.pushHomework(app, homework)
            } catch (e: Exception) {
                Log.w("ZaparaWidget", "apply", e)
            }
        }
    }

    private fun night(context: Context): Boolean {
        val mode = context.resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK
        return mode == Configuration.UI_MODE_NIGHT_YES
    }
}
