package ru.bgtu_voenmeh.zapara.ui.widgets

import android.app.AlarmManager
import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.content.BroadcastReceiver
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.res.Configuration
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
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
import java.time.LocalDateTime
import java.time.ZoneId

object WidgetUpdater {
    private val gate = Any()
    private var bound = false
    private var screenWatch: BroadcastReceiver? = null
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val main = Handler(Looper.getMainLooper())
    private val restoreLock = Mutex()

    fun bind(app: ZaparaApplication) {
        synchronized(gate) {
            if (bound) return
            bound = true
        }
        watchScreen(app)
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

    fun pulse(context: Context) {
        val app = context.applicationContext as? ZaparaApplication ?: return
        bind(app)
        scope.launch { repaintTimer(app) }
    }

    private fun watchScreen(app: Context) {
        main.post {
            if (screenWatch != null) return@post
            val receiver = object : BroadcastReceiver() {
                override fun onReceive(ctx: Context, intent: Intent?) {
                    if (timerPlaced(ctx)) pulse(ctx)
                }
            }
            val filter = IntentFilter(Intent.ACTION_SCREEN_ON)
            if (Build.VERSION.SDK_INT >= 33) {
                app.applicationContext.registerReceiver(receiver, filter, Context.RECEIVER_NOT_EXPORTED)
            } else {
                app.applicationContext.registerReceiver(receiver, filter)
            }
            screenWatch = receiver
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
                HomeworkWidgetComposer.cleared(identity, copy, dark),
                TimerWidgetComposer.cleared(identity, copy, dark)
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
            val timer = try {
                WidgetSnapshots.timer(container, identity, false, night)
            } catch (e: Exception) {
                Log.w("ZaparaWidget", "timer", e)
                null
            }
            val current = WidgetJobIdentity.of(app.host.container.profile, app.host.generation.value)
            if (!WidgetJobs.canApply(ticket, identity, current)) return
            apply(app, schedule, homework, timer)
        }
    }

    private fun apply(
        app: ZaparaApplication,
        schedule: ScheduleWidgetSnapshot,
        homework: HomeworkWidgetSnapshot,
        timer: TimerWidgetSnapshot?
    ) {
        main.post {
            val current = WidgetJobIdentity.of(app.host.container.profile, app.host.generation.value)
            if (!WidgetJobs.accept(schedule.identity, current)) return@post
            if (!WidgetJobs.accept(homework.identity, current)) return@post
            if (timer != null && !WidgetJobs.accept(timer.identity, current)) return@post
            try {
                WidgetRemoteViews.pushSchedule(app, schedule)
                WidgetRemoteViews.pushHomework(app, homework)
                if (timer != null) WidgetRemoteViews.pushTimer(app, timer)
                val phaseEnd = widgetWakeAt(
                    schedule.nextRefreshAt,
                    timer?.endsAt,
                    timer?.nextRefreshAt,
                    timerPlaced(app),
                    timer?.cleared != false
                )
                scheduleAdvance(app, phaseEnd)
                followTimer(app, timer)
            } catch (e: Exception) {
                Log.w("ZaparaWidget", "apply", e)
            }
        }
    }

    private fun timerPlaced(context: Context): Boolean = try {
        val ids = AppWidgetManager.getInstance(context)
            .getAppWidgetIds(ComponentName(context, TimerWidgetProvider::class.java))
        ids.isNotEmpty()
    } catch (_: Exception) {
        false
    }

    private fun repaintTimer(app: ZaparaApplication) {
        val container = app.host.container
        if (container.closed) return
        val identity = WidgetJobIdentity.of(container.profile, app.host.generation.value)
        val night = night(app)
        container.work.enter().use { ticket ->
            if (!ticket.admitted) {
                keepPulse(app)
                return
            }
            val timer = try {
                WidgetSnapshots.timer(container, identity, false, night)
            } catch (e: Exception) {
                Log.w("ZaparaWidget", "timer", e)
                keepPulse(app)
                return
            }
            val current = WidgetJobIdentity.of(app.host.container.profile, app.host.generation.value)
            if (!WidgetJobs.canApply(ticket, identity, current)) {
                keepPulse(app)
                return
            }
            main.post {
                val nowId = WidgetJobIdentity.of(app.host.container.profile, app.host.generation.value)
                if (!WidgetJobs.accept(timer.identity, nowId)) {
                    keepPulse(app)
                    return@post
                }
                try {
                    WidgetRemoteViews.pushTimer(app, timer)
                    followTimer(app, timer)
                } catch (e: Exception) {
                    Log.w("ZaparaWidget", "timer", e)
                    keepPulse(app)
                }
            }
        }
    }

    private fun keepPulse(app: ZaparaApplication) {
        main.post {
            if (timerPlaced(app)) schedulePulse(app, LocalDateTime.now().plusSeconds(2))
        }
    }

    private fun followTimer(context: Context, timer: TimerWidgetSnapshot?) {
        val end = timer?.endsAt
        val counting = timer != null && !timer.cleared && end != null && end.isAfter(LocalDateTime.now()) && timerPlaced(context)
        schedulePulse(context, if (counting) end else null)
    }

    private fun schedulePulse(context: Context, end: LocalDateTime?) {
        val am = context.getSystemService(Context.ALARM_SERVICE) as AlarmManager
        val intent = Intent(context, TimerWidgetProvider::class.java).setAction(TimerWidgetProvider.ACTION_PULSE)
        val pending = PendingIntent.getBroadcast(
            context,
            TimerWidgetProvider.PULSE,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        am.cancel(pending)
        if (end == null) return
        val bell = end.atZone(ZoneId.systemDefault()).toInstant().toEpochMilli()
        val exact = Build.VERSION.SDK_INT < 31 || am.canScheduleExactAlarms()
        val interactive = (context.getSystemService(Context.POWER_SERVICE) as PowerManager).isInteractive
        val delay = timerPulseDelayMs(interactive, exact, bell - System.currentTimeMillis()) ?: return
        try {
            // RTC, not RTC_WAKEUP: the screen-off tick must not wake the phone.
            // The phase boundary still wakes through scheduleAdvance.
            am.setExact(AlarmManager.RTC, System.currentTimeMillis() + delay, pending)
        } catch (e: SecurityException) {
            Log.w("ZaparaWidget", "pulse", e)
        }
    }

    private fun scheduleAdvance(context: Context, at: LocalDateTime?) {
        val am = context.getSystemService(Context.ALARM_SERVICE) as AlarmManager
        val intent = Intent(context, ScheduleWidgetProvider::class.java).setAction(ScheduleWidgetProvider.ACTION_ADVANCE)
        val pending = PendingIntent.getBroadcast(
            context,
            ScheduleWidgetProvider.REQUEST,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        am.cancel(pending)
        if (at == null) return
        val due = at.atZone(ZoneId.systemDefault()).toInstant().toEpochMilli()
        val millis = if (due <= System.currentTimeMillis()) System.currentTimeMillis() + 1_000 else due
        if (Build.VERSION.SDK_INT >= 31 && !am.canScheduleExactAlarms()) {
            am.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, millis, pending)
        } else {
            am.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, millis, pending)
        }
    }

    private fun night(context: Context): Boolean {
        val mode = context.resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK
        return mode == Configuration.UI_MODE_NIGHT_YES
    }
}
