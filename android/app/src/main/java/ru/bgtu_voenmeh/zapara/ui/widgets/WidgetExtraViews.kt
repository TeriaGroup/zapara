package ru.bgtu_voenmeh.zapara.ui.widgets

import android.content.Context
import android.view.View
import android.widget.RemoteViews
import ru.bgtu_voenmeh.zapara.R
import java.time.format.DateTimeFormatter
import java.util.Locale

object WidgetExtraViews {
    private val spokenDate = DateTimeFormatter.ofPattern("d MMMM, EEEE", Locale.forLanguageTag("ru"))
    internal val weekCells = listOf(
        R.id.widget_week_day1, R.id.widget_week_day2, R.id.widget_week_day3, R.id.widget_week_day4,
        R.id.widget_week_day5, R.id.widget_week_day6, R.id.widget_week_day7
    )

    fun wayfinder(context: Context, snapshot: WayfinderWidgetSnapshot, widgetId: Int): RemoteViews {
        val views = RemoteViews(context.packageName, R.layout.widget_wayfinder)
        val colors = WidgetPalette.of(context, snapshot.isDark)
        views.setInt(R.id.widget_wayfinder_root, "setBackgroundResource", colors.background)
        line(views, R.id.widget_wayfinder_title, snapshot.title, colors.text1)
        line(views, R.id.widget_wayfinder_status, snapshot.status, colors.text2)
        line(views, R.id.widget_wayfinder_room, snapshot.room, colors.text1)
        line(views, R.id.widget_wayfinder_subject, snapshot.subject, colors.text1)
        line(views, R.id.widget_wayfinder_time, snapshot.time, colors.text2)
        line(views, R.id.widget_wayfinder_empty, if (snapshot.cleared) "" else snapshot.empty.orEmpty(), colors.text2)
        views.setViewVisibility(R.id.widget_wayfinder_overlay, View.GONE)
        val spoken = listOf(snapshot.title, snapshot.status, snapshot.targetDate?.format(spokenDate).orEmpty(),
            snapshot.subject, snapshot.time, snapshot.room, snapshot.empty.orEmpty())
            .filter(String::isNotBlank).joinToString(", ")
        views.setContentDescription(R.id.widget_wayfinder_root, spoken)
        views.setOnClickPendingIntent(R.id.widget_wayfinder_root, WidgetIntents.open(context, widgetId, 0,
            if (snapshot.opensMap) "maps" else "schedule",
            if (snapshot.opensMap) snapshot.classroomRaw else snapshot.targetDate?.toString()))
        return views
    }

    fun week(context: Context, snapshot: WeekWidgetSnapshot, widgetId: Int): RemoteViews {
        val views = RemoteViews(context.packageName, R.layout.widget_week)
        val colors = WidgetPalette.of(context, snapshot.isDark)
        views.setInt(R.id.widget_week_root, "setBackgroundResource", colors.background)
        line(views, R.id.widget_week_title, snapshot.title, colors.text1)
        val empty = if (snapshot.cleared) "" else snapshot.empty.orEmpty()
        line(views, R.id.widget_week_subtitle, snapshot.subtitle.takeIf { empty.isEmpty() }.orEmpty(), colors.text2)
        line(views, R.id.widget_week_empty, empty, colors.text2)
        views.setContentDescription(R.id.widget_week_root,
            listOf(snapshot.title, snapshot.subtitle, empty).filter(String::isNotBlank).joinToString(", "))
        views.setViewVisibility(R.id.widget_week_overlay, View.GONE)
        weekCells.forEachIndexed { index, id ->
            val day = snapshot.days.getOrNull(index).takeUnless { snapshot.cleared }
            val count = day?.let { context.resources.getQuantityString(R.plurals.widget_week_pairs, it.lessonCount, it.lessonCount) }.orEmpty()
            line(views, id, day?.let { "${it.shortName} ${it.date.dayOfMonth}\n$count" }.orEmpty(),
                if (day?.isToday == true) colors.onAccent else if (day?.lessonCount == 0) colors.text2 else colors.text1)
            val background = if (day?.isToday == true) {
                if (snapshot.isDark) R.drawable.widget_day_today_dark else R.drawable.widget_day_today_light
            } else 0
            views.setInt(id, "setBackgroundResource", background)
            val spoken = if (day == null) "" else listOf(
                day.date.format(spokenDate), count,
                context.getString(R.string.widget_week_today).takeIf { day.isToday }.orEmpty()
            ).filter(String::isNotBlank).joinToString(", ")
            views.setContentDescription(id, spoken)
            views.setOnClickPendingIntent(id, day?.let {
                WidgetIntents.open(context, widgetId, index + 1, "schedule", it.date.toString())
            })
        }
        views.setOnClickPendingIntent(R.id.widget_week_root, WidgetIntents.open(context, widgetId, 0, "schedule", null))
        return views
    }

    private fun line(views: RemoteViews, id: Int, text: String, color: Int) {
        views.setTextViewText(id, text)
        views.setTextColor(id, color)
        views.setViewVisibility(id, if (text.isBlank()) View.GONE else View.VISIBLE)
    }
}
