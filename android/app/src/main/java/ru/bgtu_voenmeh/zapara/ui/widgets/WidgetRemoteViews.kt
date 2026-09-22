package ru.bgtu_voenmeh.zapara.ui.widgets

import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.util.TypedValue
import android.view.View
import android.widget.RemoteViews
import ru.bgtu_voenmeh.zapara.MainActivity
import ru.bgtu_voenmeh.zapara.R
import java.time.Duration
import java.time.LocalDateTime

object WidgetRemoteViews {
    private data class ScheduleSlot(val row: Int, val name: Int, val meta: Int, val number: Int)

    private val scheduleRows = listOf(
        ScheduleSlot(R.id.widget_schedule_row1, R.id.widget_schedule_name1, R.id.widget_schedule_meta1, R.id.widget_schedule_num1),
        ScheduleSlot(R.id.widget_schedule_row2, R.id.widget_schedule_name2, R.id.widget_schedule_meta2, R.id.widget_schedule_num2),
        ScheduleSlot(R.id.widget_schedule_row3, R.id.widget_schedule_name3, R.id.widget_schedule_meta3, R.id.widget_schedule_num3),
        ScheduleSlot(R.id.widget_schedule_row4, R.id.widget_schedule_name4, R.id.widget_schedule_meta4, R.id.widget_schedule_num4)
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
        views.setContentDescription(R.id.widget_schedule_root, snapshot.title.ifBlank { context.getString(R.string.nav_schedule) })
        views.setViewVisibility(R.id.widget_schedule_toss, View.GONE)
        scheduleRows.forEachIndexed { index, slot ->
            val row = snapshot.rows.getOrNull(index)
            val bind = WidgetRowBind.of(row)
            views.setTextViewText(slot.name, bind.primary)
            views.setTextViewText(slot.meta, bind.secondary)
            views.setTextViewText(slot.number, if (row != null && row.number > 0) row.number.toString() else "")
            views.setViewVisibility(slot.row, if (bind.visible) View.VISIBLE else View.GONE)
            if (bind.visible && row != null) {
                val ink = if (row.isPast) colors.text3 else colors.text1
                views.setTextColor(slot.name, ink)
                views.setTextColor(slot.meta, colors.text2)
                views.setTextColor(slot.number, colors.text2)
            }
        }
        val open = openApp(context, 4101, "schedule")
        views.setOnClickPendingIntent(R.id.widget_schedule_root, open)
        views.setOnClickPendingIntent(R.id.widget_schedule_body, open)
        views.setOnClickPendingIntent(R.id.widget_schedule_toss, open)
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

    fun timer(context: Context, snapshot: TimerWidgetSnapshot, widthDp: Int = 110): RemoteViews {
        val views = RemoteViews(context.packageName, R.layout.widget_timer)
        val colors = WidgetPalette.of(context, snapshot.isDark)
        views.setInt(R.id.widget_timer_root, "setBackgroundResource", colors.background)
        val arc = if (snapshot.kind == TimerPhaseKind.Break) colors.warn else colors.ok
        val fraction = if (snapshot.cleared) 0f else snapshot.fraction
        views.setImageViewBitmap(
            R.id.widget_timer_ring,
            TimerRing.bitmap(context, widthDp, fraction, colors.text3, arc)
        )
        bindClock(views, snapshot, colors, widthDp)
        val phaseColor = when (snapshot.kind) {
            TimerPhaseKind.Lesson -> colors.ok
            TimerPhaseKind.Break -> colors.warn
            else -> colors.text2
        }
        bindLine(views, R.id.widget_timer_phase, snapshot.phaseText, phaseColor)
        bindLine(views, R.id.widget_timer_subject, snapshot.subject, colors.text1)
        bindLine(views, R.id.widget_timer_detail, snapshot.detail, colors.text2)
        val spoken = listOf(snapshot.phaseText, snapshot.timeText, snapshot.subject, snapshot.detail)
            .map { it.trim() }
            .filter { it.isNotEmpty() }
            .joinToString(", ")
        views.setContentDescription(
            R.id.widget_timer_root,
            spoken.ifEmpty { context.getString(R.string.widget_timer_label) }
        )
        views.setOnClickPendingIntent(R.id.widget_timer_root, openApp(context, 4104, "schedule"))
        return views
    }

    fun pushSchedule(context: Context, snapshot: ScheduleWidgetSnapshot) {
        val mgr = AppWidgetManager.getInstance(context)
        val ids = mgr.getAppWidgetIds(ComponentName(context, ScheduleWidgetProvider::class.java))
        if (ids.isEmpty()) return
        val profile = "${snapshot.identity.profileId}:${snapshot.identity.databaseName}"
        val scale = runCatching {
            android.provider.Settings.Global.getFloat(
                context.contentResolver,
                android.provider.Settings.Global.ANIMATOR_DURATION_SCALE,
                1f
            )
        }.getOrDefault(1f)
        ids.forEach { id ->
            ScheduleTossPlayer.cancel(id)
            val opts = mgr.getAppWidgetOptions(id)
            val height = opts.getInt(AppWidgetManager.OPTION_APPWIDGET_MIN_HEIGHT, 160).let { if (it > 0) it else 160 }
            val width = opts.getInt(AppWidgetManager.OPTION_APPWIDGET_MIN_WIDTH, 180).let { if (it > 0) it else 180 }
            val cap = ScheduleWidgetComposer.rowsForHeightDp(height)
            val rows = snapshot.rows.take(cap)
            mgr.updateAppWidget(id, schedule(context, snapshot.copy(rows = rows)))
            if (snapshot.cleared) return@forEach
            val saved = ScheduleWidgetMemory.read(context, id)
            val toss = snapshot.toss
            val play = shouldTossSchedule(
                scheduleTossFits(width, height),
                scale,
                saved?.first,
                saved?.second ?: emptySet(),
                profile,
                toss?.faceKey()
            )
            ScheduleWidgetMemory.write(context, id, profile, rows.map { it.faceKey() })
            if (play && toss != null) ScheduleTossPlayer.play(context, id, toss, width, height)
        }
    }

    fun pushHomework(context: Context, snapshot: HomeworkWidgetSnapshot) {
        val mgr = AppWidgetManager.getInstance(context)
        val ids = mgr.getAppWidgetIds(ComponentName(context, HomeworkWidgetProvider::class.java))
        if (ids.isEmpty()) return
        mgr.updateAppWidget(ids, homework(context, snapshot))
    }

    private val timerFaces = HashMap<Int, String>()

    fun dropTimerFaces() {
        timerFaces.clear()
    }

    fun pushTimer(context: Context, snapshot: TimerWidgetSnapshot) {
        val mgr = AppWidgetManager.getInstance(context)
        val ids = mgr.getAppWidgetIds(ComponentName(context, TimerWidgetProvider::class.java))
        if (ids.isEmpty()) {
            timerFaces.clear()
            return
        }
        val live = ids.toSet()
        timerFaces.keys.retainAll(live)
        ids.forEach { id ->
            val width = mgr.getAppWidgetOptions(id).getInt(AppWidgetManager.OPTION_APPWIDGET_MIN_WIDTH, 110)
            val dp = if (width > 0) width else 110
            val face = timerFace(snapshot, dp)
            if (timerFaces[id] == face) {
                mgr.partiallyUpdateAppWidget(id, moving(context, snapshot, dp))
            } else {
                mgr.updateAppWidget(id, timer(context, snapshot, dp))
                timerFaces[id] = face
            }
        }
    }

    private fun timerFace(snapshot: TimerWidgetSnapshot, widthDp: Int): String =
        listOf(snapshot.kind, snapshot.endsAt, snapshot.phaseText, snapshot.subject, snapshot.detail, snapshot.cleared, snapshot.isDark, widthDp)
            .joinToString("|")

    private fun ring(context: Context, snapshot: TimerWidgetSnapshot, widthDp: Int): RemoteViews {
        val views = RemoteViews(context.packageName, R.layout.widget_timer)
        val colors = WidgetPalette.of(context, snapshot.isDark)
        val arc = if (snapshot.kind == TimerPhaseKind.Break) colors.warn else colors.ok
        val fraction = if (snapshot.cleared || snapshot.endsAt?.isAfter(LocalDateTime.now()) != true) 0f else snapshot.fraction
        views.setImageViewBitmap(R.id.widget_timer_ring, TimerRing.bitmap(context, widthDp, fraction, colors.text3, arc))
        return views
    }

    private fun moving(context: Context, snapshot: TimerWidgetSnapshot, widthDp: Int): RemoteViews {
        val views = ring(context, snapshot, widthDp)
        bindClock(views, snapshot, WidgetPalette.of(context, snapshot.isDark), widthDp)
        return views
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

    private fun bindClock(views: RemoteViews, snapshot: TimerWidgetSnapshot, colors: WidgetPalette, widthDp: Int) {
        val end = snapshot.endsAt
        if (end == null || snapshot.cleared) {
            views.setTextViewText(R.id.widget_timer_time, "")
            views.setViewVisibility(R.id.widget_timer_time, View.GONE)
            return
        }
        val label = timerDigitText(Duration.between(LocalDateTime.now(), end).toMillis())
        val base = when {
            widthDp >= 180 -> 30f
            widthDp >= 140 -> 26f
            else -> 22f
        }
        val timeSp = if (label.length > 5) base * 0.75f else base
        views.setViewVisibility(R.id.widget_timer_time, View.VISIBLE)
        views.setTextViewTextSize(R.id.widget_timer_time, TypedValue.COMPLEX_UNIT_SP, timeSp)
        views.setTextColor(R.id.widget_timer_time, colors.text1)
        views.setTextViewText(R.id.widget_timer_time, label)
    }

    private fun bindLine(views: RemoteViews, id: Int, text: String, color: Int) {
        val shown = text.trim()
        views.setTextViewText(id, shown)
        views.setViewVisibility(id, if (shown.isEmpty()) View.GONE else View.VISIBLE)
        if (shown.isNotEmpty()) views.setTextColor(id, color)
    }

    private fun openApp(context: Context, request: Int, section: String): PendingIntent {
        val intent = Intent(context, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP
            putExtra(MainActivity.SECTION_EXTRA, section)
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
