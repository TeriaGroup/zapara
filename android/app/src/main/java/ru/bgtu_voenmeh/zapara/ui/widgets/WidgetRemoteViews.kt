package ru.bgtu_voenmeh.zapara.ui.widgets

import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.view.View
import android.widget.RemoteViews
import ru.bgtu_voenmeh.zapara.MainActivity
import ru.bgtu_voenmeh.zapara.R

object WidgetRemoteViews {
    private val scheduleRows = listOf(
        Triple(R.id.widget_schedule_row1, R.id.widget_schedule_name1, R.id.widget_schedule_meta1),
        Triple(R.id.widget_schedule_row2, R.id.widget_schedule_name2, R.id.widget_schedule_meta2),
        Triple(R.id.widget_schedule_row3, R.id.widget_schedule_name3, R.id.widget_schedule_meta3),
        Triple(R.id.widget_schedule_row4, R.id.widget_schedule_name4, R.id.widget_schedule_meta4)
    )
    private val homeworkRows = listOf(
        Triple(R.id.widget_homework_row1, R.id.widget_homework_subject1, R.id.widget_homework_detail1),
        Triple(R.id.widget_homework_row2, R.id.widget_homework_subject2, R.id.widget_homework_detail2),
        Triple(R.id.widget_homework_row3, R.id.widget_homework_subject3, R.id.widget_homework_detail3),
        Triple(R.id.widget_homework_row4, R.id.widget_homework_subject4, R.id.widget_homework_detail4)
    )

    fun schedule(context: Context, snapshot: ScheduleWidgetSnapshot): RemoteViews {
        val views = RemoteViews(context.packageName, R.layout.widget_schedule)
        val colors = WidgetPalette.of(context, snapshot.isDark)
        paintChrome(views, R.id.widget_schedule_root, R.id.widget_schedule_title, R.id.widget_schedule_subtitle, R.id.widget_schedule_empty, snapshot.title, snapshot.subtitle, snapshot.empty, snapshot.cleared, colors)
        scheduleRows.forEachIndexed { index, ids ->
            val row = snapshot.rows.getOrNull(index)
            val bind = WidgetRowBind.of(row)
            views.setTextViewText(ids.second, bind.primary)
            views.setTextViewText(ids.third, bind.secondary)
            views.setViewVisibility(ids.first, if (bind.visible) View.VISIBLE else View.GONE)
            if (bind.visible && row != null) {
                val ink = if (row.isPast) colors.text3 else colors.text1
                views.setTextColor(ids.second, ink)
                views.setTextColor(ids.third, colors.text2)
            }
        }
        views.setOnClickPendingIntent(R.id.widget_schedule_root, openApp(context, 4101, "schedule"))
        return views
    }

    fun homework(context: Context, snapshot: HomeworkWidgetSnapshot): RemoteViews {
        val views = RemoteViews(context.packageName, R.layout.widget_homework)
        val colors = WidgetPalette.of(context, snapshot.isDark)
        paintChrome(views, R.id.widget_homework_root, R.id.widget_homework_title, R.id.widget_homework_subtitle, R.id.widget_homework_empty, snapshot.title, snapshot.subtitle, snapshot.empty, snapshot.cleared, colors)
        homeworkRows.forEachIndexed { index, ids ->
            val row = snapshot.rows.getOrNull(index)
            val bind = WidgetRowBind.of(row)
            views.setTextViewText(ids.second, bind.primary)
            views.setTextViewText(ids.third, bind.secondary)
            views.setViewVisibility(ids.first, if (bind.visible) View.VISIBLE else View.GONE)
            if (bind.visible) {
                views.setTextColor(ids.second, colors.text1)
                views.setTextColor(ids.third, colors.tone(bind.tone))
            }
        }
        views.setOnClickPendingIntent(R.id.widget_homework_root, openApp(context, 4102, "homework"))
        return views
    }

    fun pushSchedule(context: Context, snapshot: ScheduleWidgetSnapshot) {
        val mgr = AppWidgetManager.getInstance(context)
        val ids = mgr.getAppWidgetIds(ComponentName(context, ScheduleWidgetProvider::class.java))
        if (ids.isEmpty()) return
        mgr.updateAppWidget(ids, schedule(context, snapshot))
    }

    fun pushHomework(context: Context, snapshot: HomeworkWidgetSnapshot) {
        val mgr = AppWidgetManager.getInstance(context)
        val ids = mgr.getAppWidgetIds(ComponentName(context, HomeworkWidgetProvider::class.java))
        if (ids.isEmpty()) return
        mgr.updateAppWidget(ids, homework(context, snapshot))
    }

    private fun paintChrome(
        views: RemoteViews,
        root: Int,
        titleId: Int,
        subtitleId: Int,
        emptyId: Int,
        title: String,
        subtitle: String,
        empty: String?,
        cleared: Boolean,
        colors: WidgetPalette
    ) {
        views.setInt(root, "setBackgroundResource", colors.background)
        views.setTextViewText(titleId, title)
        views.setTextColor(titleId, colors.text1)
        views.setTextViewText(subtitleId, subtitle)
        views.setTextColor(subtitleId, colors.text2)
        views.setViewVisibility(subtitleId, if (subtitle.isEmpty()) View.GONE else View.VISIBLE)
        val emptyText = if (cleared) "" else empty.orEmpty()
        views.setTextViewText(emptyId, emptyText)
        if (emptyText.isEmpty()) {
            views.setViewVisibility(emptyId, View.GONE)
        } else {
            views.setViewVisibility(emptyId, View.VISIBLE)
            views.setTextColor(emptyId, colors.text2)
        }
    }

    private fun openApp(context: Context, request: Int, section: String): PendingIntent {
        val intent = Intent(context, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP
            putExtra("zapara.section", section)
        }
        return PendingIntent.getActivity(
            context,
            request,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
    }
}

internal class WidgetPalette(
    val background: Int,
    val text1: Int,
    val text2: Int,
    val text3: Int,
    val warn: Int,
    val bad: Int,
    val ok: Int
) {
    fun tone(name: String): Int = when (name) {
        "warn" -> warn
        "bad" -> bad
        "ok" -> ok
        "text3" -> text3
        else -> text2
    }

    companion object {
        fun of(context: Context, dark: Boolean): WidgetPalette {
            val res = context.resources
            fun color(light: Int, night: Int) = res.getColor(if (dark) night else light, context.theme)
            return WidgetPalette(
                background = if (dark) R.drawable.widget_bg_dark else R.drawable.widget_bg_light,
                text1 = color(R.color.widget_light_text1, R.color.widget_dark_text1),
                text2 = color(R.color.widget_light_text2, R.color.widget_dark_text2),
                text3 = color(R.color.widget_light_text3, R.color.widget_dark_text3),
                warn = color(R.color.widget_light_warn, R.color.widget_dark_warn),
                bad = color(R.color.widget_light_bad, R.color.widget_dark_bad),
                ok = color(R.color.widget_light_ok, R.color.widget_dark_ok)
            )
        }
    }
}
