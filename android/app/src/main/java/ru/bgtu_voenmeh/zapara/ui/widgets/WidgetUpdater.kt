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
import android.database.ContentObserver
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
import android.provider.Settings
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
import ru.bgtu_voenmeh.zapara.AppContainer
import java.time.LocalDateTime
import java.time.ZoneId

object WidgetUpdater {
    private val gate = Any()
    private var bound = false
    private var screenWatch: BroadcastReceiver? = null
    private var armedBell: Long = Long.MIN_VALUE
    // Accessed on main; retries are only for a failed timer read, never a clock tick.
    private var timerReadRetries = 0
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val main = Handler(Looper.getMainLooper())
    private val restoreLock = Mutex()
    // Accessed only on main. Failed reads retain the last successful boundary for this profile.
    private val profilePreparation = WidgetProfilePreparation()
    private var alarmIdentity: WidgetJobIdentity? = null
    private var scheduleWake: LocalDateTime? = null
    private var timerEnd: LocalDateTime? = null
    private var timerWake: LocalDateTime? = null
    private var wayfinderWake: LocalDateTime? = null

    data class MotionPreferenceChange(val identity: WidgetJobIdentity, val revision: Long)

    /** Called synchronously by the real preference event, before its asynchronous database save. */
    fun animationsChanging(container: AppContainer): MotionPreferenceChange? {
        val app = container.app as? ZaparaApplication ?: return null
        if (app.host.container !== container || container.closed) return null
        val identity = WidgetJobIdentity.of(container.profile, app.host.generation.value)
        return MotionPreferenceChange(identity, WidgetMotionPlayer.beginPreferenceChange(identity))
    }

    /** Read persisted state after success, failure or cancellation; only the active profile may refresh. */
    fun animationsSaved(container: AppContainer, change: MotionPreferenceChange?) {
        if (change == null) return
        val app = container.app as? ZaparaApplication ?: return
        val current = WidgetJobIdentity.of(app.host.container.profile, app.host.generation.value)
        if (app.host.container !== container || !WidgetJobs.accept(change.identity, current)) return
        WidgetMotionPlayer.finishPreferenceChange(change.identity, change.revision)
        refresh(app)
    }

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
        main.post {
            cancelAbsentAlarms(app)
            prepareProfile(app, WidgetJobIdentity.of(app.host.container.profile, app.host.generation.value))
        }
        scope.launch {
            restoreIfGuest(app)
            push(app, app.host.generation.value, clearFirst = false)
        }
    }

    fun beat(context: Context) {
        val app = context.applicationContext as? ZaparaApplication ?: return
        bind(app)
        scheduleHeartbeat(app)
        scope.launch {
            if (app.host.container.closed) return@launch
            push(app, app.host.generation.value, clearFirst = false, heartbeatOnly = true)
        }
    }

    fun reboot(context: Context) {
        val app = context.applicationContext as? ZaparaApplication ?: return
        bind(app)
        scheduleDaily(app)
        scope.launch {
            restoreIfGuest(app)
            main.post {
                armedBell = Long.MIN_VALUE
                WidgetRemoteViews.dropTimerFaces()
            }
            push(app, app.host.generation.value, clearFirst = false)
        }
    }

    fun pulse(context: Context, intent: Intent? = null) {
        val app = context.applicationContext as? ZaparaApplication ?: return
        bind(app)
        // A bell must stop the host clock before asynchronous profile data is read.
        main.post {
            val current = WidgetJobIdentity.of(app.host.container.profile, app.host.generation.value)
            if (!prepareProfile(app, current)) return@post
            val endMillis = intent?.getLongExtra(TimerWidgetProvider.EXTRA_BELL_END, Long.MIN_VALUE) ?: Long.MIN_VALUE
            val expected = timerEnd?.atZone(ZoneId.systemDefault())?.toInstant()?.toEpochMilli()
            val sameProfile = intent != null &&
                intent.getStringExtra(TimerWidgetProvider.EXTRA_BELL_PROFILE) == current.profileId &&
                intent.getStringExtra(TimerWidgetProvider.EXTRA_BELL_DATABASE) == current.databaseName &&
                intent.getLongExtra(TimerWidgetProvider.EXTRA_BELL_GENERATION, Long.MIN_VALUE) == current.generation
            val am = app.getSystemService(Context.ALARM_SERVICE) as AlarmManager
            val exact = Build.VERSION.SDK_INT < 31 || am.canScheduleExactAlarms()
            if (sameProfile && endMillis != Long.MIN_VALUE && endMillis == expected && exact) {
                publish("freeze timer") { WidgetRemoteViews.freezeTimerAtBell(app) }
            }
        }
        scope.launch { repaintTimer(app) }
    }

    private fun watchScreen(app: Context) {
        main.post {
            if (screenWatch != null) return@post
            val receiver = object : BroadcastReceiver() {
                override fun onReceive(ctx: Context, intent: Intent?) {
                    if (intent?.action == Intent.ACTION_SCREEN_OFF) {
                        WidgetMotionPlayer.cancelAll()
                        scheduleHeartbeat(ctx)
                    }
                    else refresh(ctx)
                }
            }
            val filter = IntentFilter(Intent.ACTION_SCREEN_ON).apply { addAction(Intent.ACTION_SCREEN_OFF) }
            if (Build.VERSION.SDK_INT >= 33) {
                app.applicationContext.registerReceiver(receiver, filter, Context.RECEIVER_NOT_EXPORTED)
            } else {
                app.applicationContext.registerReceiver(receiver, filter)
            }
            screenWatch = receiver
            app.contentResolver.registerContentObserver(
                Settings.Global.getUriFor(Settings.Global.ANIMATOR_DURATION_SCALE), false,
                object : ContentObserver(main) {
                    override fun onChange(selfChange: Boolean) {
                        WidgetMotionPlayer.cancelAll()
                        refresh(app)
                    }
                }
            )
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

    private fun push(app: ZaparaApplication, gen: Long, clearFirst: Boolean, heartbeatOnly: Boolean = false) {
        val container = app.host.container
        if (container.closed) return
        val identity = WidgetJobIdentity.of(container.profile, gen)
        val night = night(app)
        if (clearFirst) {
            main.post { prepareProfile(app, identity) }
        }
        container.work.enter().use { ticket ->
            if (!ticket.admitted) return
            val policyRevision = WidgetMotionPlayer.beginPolicyObservation()
            val policy = motionPolicy(app, container)
            observeMotionPolicy(app, identity, policy, policyRevision)
            val schedule = try {
                WidgetSnapshots.schedule(container, identity, false, night)
            } catch (e: Exception) {
                Log.w("ZaparaWidget", "schedule", e)
                null
            }
            val homework = if (heartbeatOnly) null else try {
                WidgetSnapshots.homework(container, identity, false, night)
            } catch (e: Exception) {
                Log.w("ZaparaWidget", "homework", e)
                null
            }
            val timer = try {
                WidgetSnapshots.timer(container, identity, false, night)
            } catch (e: Exception) {
                Log.w("ZaparaWidget", "timer", e)
                keepPulse(app)
                null
            }
            val wayfinder = if (heartbeatOnly) null else try {
                WidgetSnapshots.wayfinder(container, identity, false, night)
            } catch (e: Exception) {
                Log.w("ZaparaWidget", "wayfinder", e)
                null
            }
            val week = if (heartbeatOnly) null else try {
                WidgetSnapshots.week(container, identity, false, night)
            } catch (e: Exception) {
                Log.w("ZaparaWidget", "week", e)
                null
            }
            val current = WidgetJobIdentity.of(app.host.container.profile, app.host.generation.value)
            if (!WidgetJobs.canApply(ticket, identity, current)) return
            apply(app, identity, policy, schedule, homework, timer, wayfinder, week)
        }
    }

    private fun apply(
        app: ZaparaApplication,
        identity: WidgetJobIdentity,
        policy: WidgetMotionPolicy,
        schedule: ScheduleWidgetSnapshot?,
        homework: HomeworkWidgetSnapshot?,
        timer: TimerWidgetSnapshot?,
        wayfinder: WayfinderWidgetSnapshot?,
        week: WeekWidgetSnapshot?
    ) {
        main.post {
            if (!prepareProfile(app, identity)) return@post
            publish("motion policy") { WidgetRemoteViews.prepareMotion(app, policy) }
            scheduleDaily(app)
            scheduleHeartbeat(app)
            val current = WidgetJobIdentity.of(app.host.container.profile, app.host.generation.value)
            if (schedule != null && WidgetJobs.accept(schedule.identity, current)) publish("schedule") {
                WidgetRemoteViews.pushSchedule(app, schedule, policy)
                scheduleWake = schedule.nextRefreshAt
            }
            if (homework != null && WidgetJobs.accept(homework.identity, current)) publish("homework") {
                WidgetRemoteViews.pushHomework(app, homework, policy)
            }
            if (timer != null && WidgetJobs.accept(timer.identity, current)) publish("timer") {
                WidgetRemoteViews.pushTimer(app, timer, policy)
                timerEnd = timer.endsAt.takeUnless { timer.cleared }
                timerWake = timer.nextRefreshAt.takeUnless { timer.cleared }
                followTimer(app, timer)
            }
            if (wayfinder != null && WidgetJobs.accept(wayfinder.identity, current)) publish("wayfinder") {
                WidgetRemoteViews.pushWayfinder(app, wayfinder, policy)
                wayfinderWake = wayfinder.nextRefreshAt
            }
            if (week != null && WidgetJobs.accept(week.identity, current)) publish("week") {
                WidgetRemoteViews.pushWeek(app, week, policy)
            }
            val placed = presence(app)
            val now = LocalDateTime.now()
            // A failed read must not turn a past last-good boundary into a one-second alarm loop.
            fun LocalDateTime?.future() = this?.takeIf { it.isAfter(now) }
            val phaseEnd = placed.advanceAt(scheduleWake.future(), timerEnd.future(), timerWake.future(), false, wayfinderWake.future())
            scheduleAdvance(app, phaseEnd)
            cancelAbsentAlarms(app)
        }
    }

    private fun publish(name: String, block: () -> Unit) {
        try {
            block()
        } catch (e: Exception) {
            Log.w("ZaparaWidget", name, e)
        }
    }

    private fun prepareProfile(app: ZaparaApplication, identity: WidgetJobIdentity): Boolean {
        val container = app.host.container
        val current = WidgetJobIdentity.of(container.profile, app.host.generation.value)
        if (!WidgetJobs.accept(identity, current)) return false
        if (alarmIdentity != current) {
            WidgetRemoteViews.clearMotion()
            alarmIdentity = current
            timerReadRetries = 0
            scheduleWake = null
            timerEnd = null
            timerWake = null
            wayfinderWake = null
            WidgetRemoteViews.dropTimerFaces()
            scheduleAdvance(app, null)
            followTimer(app, null)
        }
        val copy = container.copy
        val dark = WidgetTheme.isDark("system", night(app))
        return profilePreparation.prepare(
            identity = identity,
            current = { WidgetJobIdentity.of(app.host.container.profile, app.host.generation.value) },
            readPresence = { queryPresence(app) },
            clear = { face ->
                when (face) {
                    WidgetFace.Schedule -> WidgetRemoteViews.pushSchedule(app, ScheduleWidgetComposer.cleared(identity, copy, dark))
                    WidgetFace.Homework -> WidgetRemoteViews.pushHomework(app, HomeworkWidgetComposer.cleared(identity, copy, dark))
                    WidgetFace.Timer -> WidgetRemoteViews.pushTimer(app, TimerWidgetComposer.cleared(identity, copy, dark))
                    WidgetFace.Wayfinder -> WidgetRemoteViews.pushWayfinder(app, WayfinderWidgetComposer.cleared(identity, copy, dark))
                    WidgetFace.Week -> WidgetRemoteViews.pushWeek(app, WeekWidgetComposer.cleared(identity, copy, dark))
                }
            },
            onFailure = { face, error -> Log.w("ZaparaWidget", "clear $face", error) },
            beforeClear = { WidgetRemoteViews.clearMotion() }
        )
    }

    private fun cancelAbsentAlarms(context: Context) {
        publish("prune motion") { WidgetMotionPlayer.prune(context) }
        val placed = presence(context)
        if (!placed.needsAdvance) scheduleAdvance(context, null)
        if (!placed.timer) followTimer(context, null)
        if (!placed.schedule && !placed.timer) scheduleHeartbeat(context)
        if (!placed.any) scheduleDaily(context)
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
            val policyRevision = WidgetMotionPlayer.beginPolicyObservation()
            val policy = motionPolicy(app, container)
            observeMotionPolicy(app, identity, policy, policyRevision)
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
                    if (!prepareProfile(app, timer.identity)) return@post
                    WidgetRemoteViews.pushTimer(app, timer, policy)
                    timerEnd = timer.endsAt.takeUnless { timer.cleared }
                    timerWake = timer.nextRefreshAt.takeUnless { timer.cleared }
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
            if (timerPlaced(app) && timerReadRetries < 3) {
                timerReadRetries++
                scheduleTimerRetry(app, 2_000L * timerReadRetries)
            }
        }
    }

    private fun followTimer(context: Context, timer: TimerWidgetSnapshot?) {
        timerReadRetries = 0
        scheduleTimerRetry(context, null)
        val end = timer?.endsAt
        val counting = timer != null && !timer.cleared && end != null && end.isAfter(LocalDateTime.now()) && timerPlaced(context)
        val phaseEnd = if (counting) end else null
        scheduleBell(context, phaseEnd, timer?.identity)
    }

    private fun scheduleBell(context: Context, end: LocalDateTime?, identity: WidgetJobIdentity?) {
        val am = context.getSystemService(Context.ALARM_SERVICE) as AlarmManager
        val millis = end?.atZone(ZoneId.systemDefault())?.toInstant()?.toEpochMilli() ?: Long.MIN_VALUE
        val intent = Intent(context, TimerWidgetProvider::class.java).setAction(TimerWidgetProvider.ACTION_PULSE)
        if (millis != Long.MIN_VALUE && identity != null) {
            intent.putExtra(TimerWidgetProvider.EXTRA_BELL_END, millis)
            intent.putExtra(TimerWidgetProvider.EXTRA_BELL_PROFILE, identity.profileId)
            intent.putExtra(TimerWidgetProvider.EXTRA_BELL_DATABASE, identity.databaseName)
            intent.putExtra(TimerWidgetProvider.EXTRA_BELL_GENERATION, identity.generation)
        }
        val pending = PendingIntent.getBroadcast(
            context,
            TimerWidgetProvider.BELL,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        if (millis == armedBell && millis > System.currentTimeMillis()) return
        am.cancel(pending)
        armedBell = Long.MIN_VALUE
        if (end == null || millis <= System.currentTimeMillis()) return
        val show = PendingIntent.getActivity(
            context,
            4107,
            Intent(context, ru.bgtu_voenmeh.zapara.MainActivity::class.java).addFlags(
                Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP
            ),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        try {
            if (Build.VERSION.SDK_INT >= 31 && !am.canScheduleExactAlarms()) {
                am.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, millis, pending)
            } else {
                // Not the once-a-minute idle quota. The status-bar clock is the trade for a bell that is not deferred.
                am.setAlarmClock(AlarmManager.AlarmClockInfo(millis, show), pending)
            }
            armedBell = millis
        } catch (e: SecurityException) {
            Log.w("ZaparaWidget", "bell", e)
            am.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, millis, pending)
            armedBell = millis
        }
    }

    private fun scheduleTimerRetry(context: Context, delayMs: Long?) {
        val am = context.getSystemService(Context.ALARM_SERVICE) as AlarmManager
        val intent = Intent(context, TimerWidgetProvider::class.java).setAction(TimerWidgetProvider.ACTION_PULSE)
        val pending = PendingIntent.getBroadcast(
            context,
            TimerWidgetProvider.PULSE,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        am.cancel(pending)
        if (delayMs != null) am.set(AlarmManager.RTC, System.currentTimeMillis() + delayMs, pending)
    }

    private fun scheduleHeartbeat(context: Context) {
        val am = context.getSystemService(Context.ALARM_SERVICE) as AlarmManager
        val intent = Intent(context, ScheduleWidgetProvider::class.java).setAction(ScheduleWidgetProvider.ACTION_HEARTBEAT)
        val pending = PendingIntent.getBroadcast(
            context,
            ScheduleWidgetProvider.HEARTBEAT,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        am.cancel(pending)
        val placed = presence(context)
        if (!placed.schedule && !placed.timer) return
        val exact = Build.VERSION.SDK_INT < 31 || am.canScheduleExactAlarms()
        val interactive = (context.getSystemService(Context.POWER_SERVICE) as PowerManager).isInteractive
        val delay = widgetHeartbeatMs(interactive, exact) ?: return
        try {
            if (exact) am.setExact(AlarmManager.RTC, System.currentTimeMillis() + delay, pending)
            else am.set(AlarmManager.RTC, System.currentTimeMillis() + delay, pending)
        } catch (e: SecurityException) {
            Log.w("ZaparaWidget", "heartbeat", e)
        }
    }

    private fun scheduleDaily(context: Context) {
        val am = context.getSystemService(Context.ALARM_SERVICE) as AlarmManager
        val intent = Intent(context, ScheduleWidgetProvider::class.java).setAction(ScheduleWidgetProvider.ACTION_REBOOT)
        val pending = PendingIntent.getBroadcast(
            context,
            ScheduleWidgetProvider.DAILY,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        am.cancel(pending)
        if (!anyWidget(context)) return
        val due = nextWidgetReboot(LocalDateTime.now()).atZone(ZoneId.systemDefault()).toInstant().toEpochMilli()
        val millis = if (due <= System.currentTimeMillis()) System.currentTimeMillis() + 60_000 else due
        if (Build.VERSION.SDK_INT >= 31 && !am.canScheduleExactAlarms()) {
            am.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, millis, pending)
        } else {
            am.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, millis, pending)
        }
    }

    private fun anyWidget(context: Context): Boolean = presence(context).any

    private fun presence(context: Context): WidgetPresence = try {
        queryPresence(context)
    } catch (_: Exception) {
        WidgetPresence(false, false, false, false, false)
    }

    /** Called only by the IO worker; one immutable decision travels with its snapshots. */
    private fun motionPolicy(context: Context, container: AppContainer): WidgetMotionPolicy = try {
        WidgetMotionPolicy.of(
            container.repo.settings().animations,
            Settings.Global.getFloat(context.contentResolver, Settings.Global.ANIMATOR_DURATION_SCALE, 1f),
            (context.getSystemService(Context.POWER_SERVICE) as PowerManager).isInteractive
        )
    } catch (error: Exception) {
        Log.w("ZaparaWidget", "motion policy", error)
        WidgetMotionPolicy.Disabled
    }

    private fun observeMotionPolicy(app: ZaparaApplication, identity: WidgetJobIdentity, policy: WidgetMotionPolicy, revision: Long) {
        main.post {
            val current = WidgetJobIdentity.of(app.host.container.profile, app.host.generation.value)
            if (WidgetJobs.accept(identity, current)) WidgetMotionPlayer.updatePolicy(identity, policy, revision)
        }
    }

    private fun queryPresence(context: Context): WidgetPresence {
        val mgr = AppWidgetManager.getInstance(context)
        fun placed(provider: Class<*>) = mgr.getAppWidgetIds(ComponentName(context, provider)).isNotEmpty()
        return WidgetPresence(placed(ScheduleWidgetProvider::class.java), placed(HomeworkWidgetProvider::class.java),
            placed(TimerWidgetProvider::class.java), placed(WayfinderWidgetProvider::class.java), placed(WeekWidgetProvider::class.java))
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
        if (at == null || !presence(context).needsAdvance) return
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
