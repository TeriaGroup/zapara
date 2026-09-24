package ru.bgtu_voenmeh.zapara.data

import android.app.AlarmManager
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import ru.bgtu_voenmeh.zapara.MainActivity
import ru.bgtu_voenmeh.zapara.NotificationReceiver
import ru.bgtu_voenmeh.zapara.R
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.ZoneId

// Time1 (evening) -> tomorrow's lessons, time2 (morning) -> today's lessons.
object Notifications {
    const val CHANNEL = "zapara_schedule"
    const val REQ_1 = 1001
    const val REQ_2 = 1002
    private const val ACTION = "ru.zapara.app.NOTIFY"

    fun isValidTime(t: String): Boolean = parseTime(t) != null

    fun parseTime(t: String?): LocalTime? {
        if (t.isNullOrBlank()) return null
        return try {
            val parts = t.trim().split(":")
            if (parts.size != 2) return null
            val h = parts[0].toInt()
            val m = parts[1].toInt()
            if (h !in 0..23 || m !in 0..59) return null
            LocalTime.of(h, m)
        } catch (_: Exception) {
            null
        }
    }

    fun ensureChannel(ctx: Context) {
        val nm = ctx.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        if (nm.getNotificationChannel(CHANNEL) == null) {
            nm.createNotificationChannel(
                NotificationChannel(CHANNEL, ctx.getString(R.string.notification_channel), NotificationManager.IMPORTANCE_DEFAULT)
            )
        }
    }

    /** (Re)schedule both daily alarms from stored settings. Call off the main thread. */
    fun schedule(ctx: Context) {
        val app = ctx.applicationContext
        val am = app.getSystemService(Context.ALARM_SERVICE) as AlarmManager
        cancel(app)
        val s = ScheduleRepository.get(app).settings()
        if (!s.notifyEnabled) return
        if (Build.VERSION.SDK_INT >= 31 && !am.canScheduleExactAlarms()) return
        listOf(s.notifyTime1 to REQ_1, s.notifyTime2 to REQ_2).forEach { (t, req) ->
            val lt = parseTime(t) ?: return@forEach
            am.setExactAndAllowWhileIdle(
                AlarmManager.RTC_WAKEUP,
                nextTriggerMillis(lt),
                pending(app, req, t!!)
            )
        }
    }

    fun cancel(ctx: Context) {
        val app = ctx.applicationContext
        val am = app.getSystemService(Context.ALARM_SERVICE) as AlarmManager
        am.cancel(pending(app, REQ_1, ""))
        am.cancel(pending(app, REQ_2, ""))
    }

    private fun nextTriggerMillis(t: LocalTime): Long {
        val now = LocalDateTime.now()
        var dt = now.toLocalDate().atTime(t)
        if (!dt.isAfter(now.plusMinutes(1))) dt = dt.plusDays(1)
        return dt.atZone(ZoneId.systemDefault()).toInstant().toEpochMilli()
    }

    private fun pending(ctx: Context, req: Int, time: String): PendingIntent {
        val i = Intent(ctx, NotificationReceiver::class.java)
            .setAction("$ACTION.$req")
            .putExtra("time", time)
        return PendingIntent.getBroadcast(
            ctx, req, i,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
    }

    /** Build + post the notification for a fired alarm. Call off the main thread. */
    fun showForTime(appCtx: Context, time: String?) {
        try {
            ensureChannel(appCtx)
            val repo = ScheduleRepository.get(appCtx)
            val s = repo.settings()
            if (!s.notifyEnabled) return
            val gid = s.myGroupId ?: return
            val today = LocalDate.now()
            val clock = notificationClock(today, time, s.notifyTime1)
            val date = clock.content
            val overrides = OverrideService(repo.db.overrideDao())
            val profileKey = (appCtx.applicationContext as? ru.bgtu_voenmeh.zapara.ZaparaApplication)
                ?.container?.profile?.databaseName
                ?: ru.bgtu_voenmeh.zapara.data.profiles.ProfileDescriptor.GUEST_DB
            val choices = ru.bgtu_voenmeh.zapara.data.SubgroupStore(appCtx).read(profileKey, gid)
            val homework = HomeworkService(
                repo.db.homeworkDao(),
                lessonsFor = { g, dow, parity ->
                    HomeworkDue.lessonsOnChosenDay(repo.allForGroup(g), if (g == gid) choices else emptyMap(), dow, parity)
                },
                ctx = { SchedCtx(s.myGroupId.orEmpty(), s.periodStart, s.weekCount, s.parityInvert) }
            )
            try { homework.recomputeAll(clock.homework) } catch (_: Exception) {}
            val raw = repo.allForGroup(gid)
            val lessons = Schedule.lessonsForDate(
                ru.bgtu_voenmeh.zapara.data.Subgroups.visible(raw, choices),
                gid, date, s.periodStart, s.weekCount, s.parityInvert
            )
            val text = NotificationText.build(
                date = date,
                groupId = gid,
                lessons = lessons,
                displayOf = { l -> overrides.displayName(l.subjectRaw, l.dayOfWeek).ifEmpty { l.subjectRaw } },
                burningMark = { l ->
                    homework.forSubject(l.subjectRaw)
                        .firstOrNull { it.status == "burning" || it.status == "burning_urgent" }
                        ?.let { appCtx.getString(R.string.notification_homework_mark) }
                },
                isOdd = Parity.isOddWeek(date, s.periodStart, s.weekCount, s.parityInvert),
                dayName = { d -> NotificationText.localDayName(d) },
                parityName = { odd -> appCtx.getString(if (odd) R.string.notification_odd else R.string.notification_even) },
                noLessonsText = appCtx.getString(R.string.notification_no_lessons)
            )
            val openApp = PendingIntent.getActivity(
                appCtx, 0,
                Intent(appCtx, MainActivity::class.java).apply {
                    flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP
                    putExtra(MainActivity.SECTION_EXTRA, "schedule")
                },
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
            )
            val title = appCtx.getString(R.string.notification_title)
            val n = android.app.Notification.Builder(appCtx, CHANNEL)
                .setContentTitle(title)
                .setContentText(text)
                .setStyle(android.app.Notification.BigTextStyle().bigText(text))
                .setSmallIcon(R.drawable.ic_notification)
                .setContentIntent(openApp)
                .setVisibility(android.app.Notification.VISIBILITY_PRIVATE)
                .setAutoCancel(true)
                .build()
            val nm = appCtx.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
            nm.notify(2001, n)
        } catch (_: SecurityException) {
            // POST_NOTIFICATIONS denied — user disabled, stay quiet.
        } catch (_: Exception) {
        }
    }
}
