package ru.bgtu_voenmeh.zapara.ui.widgets

import android.content.Context
import android.util.TypedValue
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
        val face = if (snapshot.cleared) snapshot.copy(
            status = "", subject = "", time = "", room = "", classroomRaw = "",
            targetDate = null, opensMap = false, empty = "", readError = null, lessonTarget = null
        ) else snapshot
        val views = RemoteViews(context.packageName, R.layout.widget_wayfinder)
        val colors = WidgetPalette.of(context, snapshot.isDark)
        val options = android.appwidget.AppWidgetManager.getInstance(context).getAppWidgetOptions(widgetId)
        val tight = !snapshot.cleared && context.resources.configuration.fontScale >= 1.2f &&
            options.getInt(android.appwidget.AppWidgetManager.OPTION_APPWIDGET_MIN_HEIGHT, 110) <= 120
        views.setInt(R.id.widget_wayfinder_root, "setBackgroundResource", colors.background)
        line(views, R.id.widget_wayfinder_title,
            if (tight) context.getString(R.string.widget_wayfinder_compact_title) else face.title, colors.text1)
        line(views, R.id.widget_wayfinder_status,
            if (face.subject.isNotBlank()) face.readError ?: face.status else face.status, colors.text2)
        line(views, R.id.widget_wayfinder_room, face.room, colors.text1)
        line(views, R.id.widget_wayfinder_subject, face.subject, colors.text1)
        views.setTextViewTextSize(R.id.widget_wayfinder_subject, TypedValue.COMPLEX_UNIT_SP, if (tight) 13f else 15f)
        line(views, R.id.widget_wayfinder_time, if (tight) "" else face.time, colors.text2)
        line(views, R.id.widget_wayfinder_empty, face.empty.orEmpty(), colors.text2)
        if (face.readError != null) {
            val retry = WidgetIntents.retry(context, widgetId, "wayfinder")
            views.setOnClickPendingIntent(R.id.widget_wayfinder_status, retry)
            views.setOnClickPendingIntent(R.id.widget_wayfinder_empty, retry)
        }
        views.setViewVisibility(R.id.widget_wayfinder_overlay, View.GONE)
        val spoken = listOf(face.title, face.readError ?: face.status, face.targetDate?.format(spokenDate).orEmpty(),
            face.subject, face.time, face.room, face.empty.orEmpty())
            .filter(String::isNotBlank).joinToString(", ")
        views.setContentDescription(R.id.widget_wayfinder_root, spoken)
        views.setOnClickPendingIntent(R.id.widget_wayfinder_root, WidgetIntents.open(context, widgetId, 0,
            if (face.opensMap) "maps" else "schedule",
            if (face.opensMap) face.classroomRaw else face.targetDate?.toString(),
            ru.bgtu_voenmeh.zapara.ui.shell.WidgetLaunchScope(face.identity.profileId, face.identity.databaseName)))
        val showLesson = !snapshot.cleared && face.lessonTarget != null && face.opensMap &&
            options.getInt(android.appwidget.AppWidgetManager.OPTION_APPWIDGET_MIN_HEIGHT, 0) >=
                (200 * context.resources.configuration.fontScale.coerceAtLeast(1f)).toInt()
        views.setViewVisibility(R.id.widget_wayfinder_lesson, if (showLesson) View.VISIBLE else View.GONE)
        views.setTextColor(R.id.widget_wayfinder_lesson, colors.text1)
        if (showLesson) views.setOnClickPendingIntent(R.id.widget_wayfinder_lesson,
            WidgetIntents.scheduleRow(context, widgetId, 2, face.lessonTarget!!,
                ru.bgtu_voenmeh.zapara.ui.shell.WidgetLaunchScope(face.identity.profileId, face.identity.databaseName)))
        return views
    }

    fun week(context: Context, snapshot: WeekWidgetSnapshot, widgetId: Int): RemoteViews {
        val views = RemoteViews(context.packageName, R.layout.widget_week)
        val colors = WidgetPalette.of(context, snapshot.isDark)
        views.setInt(R.id.widget_week_root, "setBackgroundResource", colors.background)
        line(views, R.id.widget_week_title, snapshot.title, colors.text1)
        val empty = if (snapshot.cleared) context.getString(R.string.widget_loading) else snapshot.empty.orEmpty()
        val showSubtitle = snapshot.readError != null || empty.isBlank() && context.resources.configuration.fontScale < 1.2f
        line(views, R.id.widget_week_subtitle,
            (snapshot.readError ?: snapshot.subtitle).takeIf { showSubtitle }.orEmpty(), colors.text2)
        line(views, R.id.widget_week_empty, empty, colors.text2)
        if (snapshot.readError != null) {
            val retry = WidgetIntents.retry(context, widgetId, "week")
            views.setOnClickPendingIntent(R.id.widget_week_subtitle, retry)
            views.setOnClickPendingIntent(R.id.widget_week_empty, retry)
        }
        val showDays = !snapshot.cleared && empty.isBlank() && snapshot.days.isNotEmpty()
        views.setViewVisibility(R.id.widget_week_row_top, if (showDays) View.VISIBLE else View.GONE)
        views.setViewVisibility(R.id.widget_week_row_bottom, if (showDays) View.VISIBLE else View.GONE)
        views.setContentDescription(R.id.widget_week_root,
            listOf(snapshot.title, snapshot.readError ?: snapshot.subtitle.takeUnless { snapshot.cleared }.orEmpty(), empty)
                .filter(String::isNotBlank).joinToString(", "))
        views.setViewVisibility(R.id.widget_week_overlay, View.GONE)
        weekCells.forEachIndexed { index, id ->
            val day = snapshot.days.getOrNull(index).takeUnless { snapshot.cleared }
            val count = day?.let { context.resources.getQuantityString(R.plurals.widget_week_pairs, it.lessonCount, it.lessonCount) }.orEmpty()
            val widgetOptions = android.appwidget.AppWidgetManager.getInstance(context).getAppWidgetOptions(widgetId)
            val timeFits = widgetOptions.getInt(android.appwidget.AppWidgetManager.OPTION_APPWIDGET_MIN_HEIGHT, 0) >= 180 &&
                context.resources.configuration.fontScale < 1.2f
            val span = day?.timeSpan.orEmpty().takeIf { timeFits }.orEmpty()
            views.setInt(id, "setMaxLines", if (timeFits) 3 else 2)
            views.setInt(id, "setMaxHeight", ((if (timeFits) 72 else 48) * context.resources.displayMetrics.density).toInt())
            line(views, id, day?.let { "${it.shortName} ${it.date.dayOfMonth}\n$count" + if (span.isNotBlank()) "\n$span" else "" }.orEmpty(),
                if (day?.isToday == true) colors.onAccent else if (day?.lessonCount == 0) colors.text2 else colors.text1)
            val background = if (day?.isToday == true) {
                if (snapshot.isDark) R.drawable.widget_day_today_dark else R.drawable.widget_day_today_light
            } else 0
            views.setInt(id, "setBackgroundResource", background)
            val spoken = if (day == null) "" else listOf(
                day.date.format(spokenDate), count, day.timeSpan,
                context.getString(R.string.widget_week_today).takeIf { day.isToday }.orEmpty()
            ).filter(String::isNotBlank).joinToString(", ")
            views.setContentDescription(id, spoken)
            views.setOnClickPendingIntent(id, day?.let {
                WidgetIntents.open(context, widgetId, index + 1, "schedule", it.date.toString(),
                    ru.bgtu_voenmeh.zapara.ui.shell.WidgetLaunchScope(snapshot.identity.profileId, snapshot.identity.databaseName))
            })
        }
        views.setOnClickPendingIntent(R.id.widget_week_root, WidgetIntents.open(context, widgetId, 0, "schedule", null,
            ru.bgtu_voenmeh.zapara.ui.shell.WidgetLaunchScope(snapshot.identity.profileId, snapshot.identity.databaseName)))
        return views
    }

    private fun line(views: RemoteViews, id: Int, text: String, color: Int) {
        views.setTextViewText(id, text)
        views.setTextColor(id, color)
        views.setViewVisibility(id, if (text.isBlank()) View.GONE else View.VISIBLE)
    }
}
