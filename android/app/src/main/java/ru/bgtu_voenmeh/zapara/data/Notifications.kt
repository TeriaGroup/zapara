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

// Daily schedule notifications (port of Windows NotificationService: 2 user times).
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
            val date = if (time != null && time == s.notifyTime1) today.plusDays(1) else today
            val overrides = OverrideService(repo.db.overrideDao())
            val homework = HomeworkService(
                repo.db.homeworkDao(),
                lessonsFor = { g, dow, parity ->
                    repo.allForGroup(g).filter { it.dayOfWeek == dow && (it.parity == parity || it.parity == 0) }
                },
                ctx = { SchedCtx(s.myGroupId.orEmpty(), s.periodStart, s.weekCount, s.parityInvert) }
            )
            try { homework.recomputeAll(date) } catch (_: Exception) {}
            val lessons = repo.lessonsFor(gid, date)
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
                appCtx, 0, Intent(appCtx, MainActivity::class.java),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
            )
            val title = appCtx.getString(R.string.notification_title)
            val n = android.app.Notification.Builder(appCtx, CHANNEL)
                .setContentTitle(title)
                .setContentText(text)
                .setStyle(android.app.Notification.BigTextStyle().bigText(text))
                .setSmallIcon(android.R.drawable.ic_menu_today)
                .setContentIntent(openApp)
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
