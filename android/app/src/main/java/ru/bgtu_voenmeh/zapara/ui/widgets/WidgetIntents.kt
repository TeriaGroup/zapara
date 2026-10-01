package ru.bgtu_voenmeh.zapara.ui.widgets

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.net.Uri
import ru.bgtu_voenmeh.zapara.MainActivity
import ru.bgtu_voenmeh.zapara.ui.shell.WidgetLaunchScope

object WidgetIntents {
    const val EXTRA_SCHEDULE_GROUP = "widget_schedule_group"
    const val EXTRA_SCHEDULE_TIME = "widget_schedule_time"
    const val EXTRA_SCHEDULE_SUBJECT = "widget_schedule_subject"

    fun retry(context: Context, widgetId: Int, face: String): PendingIntent {
        val intent = Intent(context, ScheduleWidgetProvider::class.java).apply {
            action = ScheduleWidgetProvider.ACTION_ADVANCE
            data = Uri.Builder().scheme("zapara-widget").authority("retry")
                .appendPath(face).appendPath(widgetId.toString()).build()
        }
        return PendingIntent.getBroadcast(context, 0, intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
    }

    fun scheduleRow(context: Context, widgetId: Int, slot: Int, row: ScheduleWidgetRow,
        scope: WidgetLaunchScope): PendingIntent? {
        val target = scheduleWidgetTarget(row, scope) ?: return null
        val intent = Intent(context, MainActivity::class.java).apply {
            data = Uri.Builder().scheme("zapara-widget").authority("schedule")
                .appendPath(widgetId.toString()).appendPath(slot.toString())
                .appendQueryParameter("date", target.date.toString()).appendQueryParameter("time", target.time)
                .appendQueryParameter("subject", target.subjectNorm).appendQueryParameter("group", target.groupId)
                .appendQueryParameter("profile", scope.profileId)
                .appendQueryParameter("database", scope.databaseName).build()
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP
            putExtra(MainActivity.SECTION_EXTRA, "schedule")
            putExtra(MainActivity.ARGUMENT_EXTRA, target.date.toString())
            putExtra(EXTRA_SCHEDULE_TIME, target.time)
            putExtra(EXTRA_SCHEDULE_SUBJECT, target.subjectNorm)
            putExtra(EXTRA_SCHEDULE_GROUP, target.groupId)
            putExtra(MainActivity.WIDGET_PROFILE_EXTRA, scope.profileId)
            putExtra(MainActivity.WIDGET_DATABASE_EXTRA, scope.databaseName)
        }
        return PendingIntent.getActivity(context, 0, intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
    }

    fun open(context: Context, widgetId: Int, slot: Int, section: String, argument: String?): PendingIntent {
        val intent = Intent(context, MainActivity::class.java).apply {
            data = Uri.parse("zapara-widget://open/$widgetId/$slot")
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP
            putExtra(MainActivity.SECTION_EXTRA, section)
            putExtra(MainActivity.ARGUMENT_EXTRA, argument)
        }
        return PendingIntent.getActivity(context, 0, intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
    }

    fun homework(context: Context, widgetId: Int, slot: Int, id: Long, scope: WidgetLaunchScope): PendingIntent {
        require(id > 0)
        val intent = Intent(context, MainActivity::class.java).apply {
            // Include target and scope in identity: updating another row/profile cannot retarget
            // a PendingIntent still held by the launcher or a delayed tap.
            data = Uri.Builder().scheme("zapara-widget").authority("open")
                .appendPath(widgetId.toString()).appendPath(slot.toString())
                .appendQueryParameter("homework", id.toString())
                .appendQueryParameter("profile", scope.profileId)
                .appendQueryParameter("database", scope.databaseName).build()
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP
            putExtra(MainActivity.SECTION_EXTRA, "homework")
            putExtra(MainActivity.ARGUMENT_EXTRA, id.toString())
            putExtra(MainActivity.WIDGET_PROFILE_EXTRA, scope.profileId)
            putExtra(MainActivity.WIDGET_DATABASE_EXTRA, scope.databaseName)
        }
        return PendingIntent.getActivity(context, 0, intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
    }
}
