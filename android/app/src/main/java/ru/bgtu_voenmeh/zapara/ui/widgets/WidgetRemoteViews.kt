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

    fun schedule(context: Context, snapshot: ScheduleWidgetSnapshot, heightDp: Int = 160): RemoteViews {
        val compact = heightDp < 120
        val views = RemoteViews(context.packageName, if (compact) R.layout.widget_schedule_compact else R.layout.widget_schedule)
        val colors = WidgetPalette.of(context, snapshot.isDark)
        paintChrome(views, R.id.widget_schedule_root, R.id.widget_schedule_title, R.id.widget_schedule_subtitle, R.id.widget_schedule_empty, snapshot.title, if (compact) "" else snapshot.subtitle, snapshot.empty, snapshot.cleared, colors)
        views.setContentDescription(R.id.widget_schedule_root, snapshot.title.ifBlank { context.getString(R.string.nav_schedule) })
        views.setViewVisibility(R.id.widget_schedule_toss, View.GONE)
        scheduleRows.forEachIndexed { index, slot ->
            val row = snapshot.rows.getOrNull(index).takeIf { index < ScheduleWidgetComposer.rowsForHeightDp(heightDp) }
            val bind = WidgetRowBind.of(row)
            views.setTextViewText(slot.name, if (compact && row != null && index == 0) "${row.name} · ${row.meta.substringBefore(" · ")}" else bind.primary)
            views.setTextViewText(slot.meta, if (compact) "" else bind.secondary)
            views.setTextViewText(slot.number, if (!compact && row != null && row.number > 0) row.number.toString() else "")
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
        return views
    }

    fun homework(context: Context, snapshot: HomeworkWidgetSnapshot, heightDp: Int = 160): RemoteViews {
        val compact = heightDp < 120
        val views = RemoteViews(context.packageName, if (compact) R.layout.widget_homework_compact else R.layout.widget_homework)
        val colors = WidgetPalette.of(context, snapshot.isDark)
        paintChrome(views, R.id.widget_homework_root, R.id.widget_homework_title, R.id.widget_homework_subtitle, R.id.widget_homework_empty, snapshot.title, if (compact) "" else snapshot.subtitle, snapshot.empty, snapshot.cleared, colors)
        homeworkRows.forEachIndexed { index, ids ->
            val row = snapshot.rows.getOrNull(index).takeIf {
                index < HomeworkWidgetComposer.rowsForHeightDp(heightDp, context.resources.configuration.fontScale)
            }
            val bind = WidgetRowBind.of(row)
            views.setTextViewText(ids.second, if (compact && row != null && index == 0) "${row.subject} · ${row.detail.substringAfterLast(" · ")}" else bind.primary)
            views.setTextViewText(ids.third, if (compact) "" else bind.secondary)
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
        views.setViewVisibility(R.id.widget_timer_overlay, View.GONE)
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
        bindTimerDescription(context, views, snapshot)
        views.setOnClickPendingIntent(R.id.widget_timer_root, openApp(context, 4104, "schedule"))
        return views
    }

    private fun bindTimerDescription(context: Context, views: RemoteViews, snapshot: TimerWidgetSnapshot) {
        val time = snapshot.endsAt?.takeUnless { snapshot.cleared }?.let {
            timerDigitText(Duration.between(LocalDateTime.now(), it).toMillis())
        }.orEmpty()
        val spoken = listOf(snapshot.phaseText, time, snapshot.subject, snapshot.detail)
            .map { it.trim() }
            .filter { it.isNotEmpty() }
            .joinToString(", ")
        views.setContentDescription(
            R.id.widget_timer_root,
            spoken.ifEmpty { context.getString(R.string.widget_timer_label) }
        )
    }

    private val scheduleFaces = WidgetMotionHistory<ScheduleWidgetSnapshot>()
    private val homeworkFaces = WidgetMotionHistory<HomeworkWidgetSnapshot>()
    private val wayfinderFaces = WidgetMotionHistory<WayfinderWidgetSnapshot>()
    private val weekFaces = WidgetMotionHistory<WeekWidgetSnapshot>()
    private val timerSnapshots = WidgetMotionHistory<TimerWidgetSnapshot>()

    fun prepareMotion(context: Context, policy: WidgetMotionPolicy) {
        val live = WidgetMotionPlayer.prepare(context, policy)
        scheduleFaces.retainIds(live)
        homeworkFaces.retainIds(live)
        wayfinderFaces.retainIds(live)
        weekFaces.retainIds(live)
        timerSnapshots.retainIds(live)
    }

    fun clearMotion() {
        WidgetMotionPlayer.clear()
        scheduleFaces.clear()
        homeworkFaces.clear()
        wayfinderFaces.clear()
        weekFaces.clear()
        timerSnapshots.clear()
    }

    fun pushSchedule(context: Context, snapshot: ScheduleWidgetSnapshot, policy: WidgetMotionPolicy = WidgetMotionPolicy.Disabled) {
        prepareMotion(context, policy)
        val mgr = AppWidgetManager.getInstance(context)
        val ids = mgr.getAppWidgetIds(ComponentName(context, ScheduleWidgetProvider::class.java))
        if (ids.isEmpty()) return
        ids.forEach { id ->
            val opts = mgr.getAppWidgetOptions(id)
            val height = opts.getInt(AppWidgetManager.OPTION_APPWIDGET_MIN_HEIGHT, 160).let { if (it > 0) it else 160 }
            val width = opts.getInt(AppWidgetManager.OPTION_APPWIDGET_MIN_WIDTH, 180).let { if (it > 0) it else 180 }
            val cap = ScheduleWidgetComposer.rowsForHeightDp(height)
            val rows = snapshot.rows.take(cap)
            val shown = snapshot.copy(rows = rows)
            val previous = scheduleFaces.previous(id, snapshot.identity)
            val layout = if (height < 120) R.layout.widget_schedule_compact else R.layout.widget_schedule
            WidgetMotionPlayer.publishFinal(context, id, layout, R.id.widget_schedule_toss,
                snapshot.identity, policy, schedule(context, shown, height), snapshot.cleared)
            if (snapshot.cleared) {
                scheduleFaces.forget(id)
                return@forEach
            }
            scheduleFaces.remember(id, snapshot.identity, shown)
            val scene = previous?.let { WidgetRowEffects.schedule(it, shown, policy) }?.sized(width, height)
            if (scene != null) {
                WidgetMotionPlayer.play(context, id, layout, R.id.widget_schedule_toss, snapshot.identity, policy) {
                    scene.bitmapAt(context, it)
                }
            }
        }
    }

    fun pushHomework(context: Context, snapshot: HomeworkWidgetSnapshot, policy: WidgetMotionPolicy = WidgetMotionPolicy.Disabled) {
        prepareMotion(context, policy)
        val mgr = AppWidgetManager.getInstance(context)
        val ids = mgr.getAppWidgetIds(ComponentName(context, HomeworkWidgetProvider::class.java))
        ids.forEach { id ->
            val opts = mgr.getAppWidgetOptions(id)
            val height = opts.getInt(AppWidgetManager.OPTION_APPWIDGET_MIN_HEIGHT, 160).let { if (it > 0) it else 160 }
            val width = opts.getInt(AppWidgetManager.OPTION_APPWIDGET_MIN_WIDTH, 180).let { if (it > 0) it else 180 }
            val shown = snapshot.copy(rows = snapshot.rows.take(
                HomeworkWidgetComposer.rowsForHeightDp(height, context.resources.configuration.fontScale)))
            val previous = homeworkFaces.previous(id, snapshot.identity)
            val layout = if (height < 120) R.layout.widget_homework_compact else R.layout.widget_homework
            WidgetMotionPlayer.publishFinal(context, id, layout, R.id.widget_homework_overlay, snapshot.identity, policy,
                homework(context, shown, height), snapshot.cleared)
            if (snapshot.cleared) {
                homeworkFaces.forget(id)
                return@forEach
            }
            homeworkFaces.remember(id, snapshot.identity, shown)
            val scene = previous?.let { WidgetRowEffects.homework(it, shown, shown.doneIds, policy) }?.sized(width, height)
            if (scene != null) {
                WidgetMotionPlayer.play(context, id, layout, R.id.widget_homework_overlay, snapshot.identity, policy) {
                    scene.bitmapAt(context, it)
                }
            }
        }
    }

    fun pushWayfinder(context: Context, snapshot: WayfinderWidgetSnapshot, policy: WidgetMotionPolicy = WidgetMotionPolicy.Disabled) {
        prepareMotion(context, policy)
        val mgr = AppWidgetManager.getInstance(context)
        mgr.getAppWidgetIds(ComponentName(context, WayfinderWidgetProvider::class.java)).forEach { id ->
            val previous = wayfinderFaces.previous(id, snapshot.identity)
            WidgetMotionPlayer.publishFinal(context, id, R.layout.widget_wayfinder, R.id.widget_wayfinder_overlay, snapshot.identity, policy,
                WidgetExtraViews.wayfinder(context, snapshot, id), snapshot.cleared)
            if (snapshot.cleared) wayfinderFaces.forget(id) else {
                wayfinderFaces.remember(id, snapshot.identity, snapshot)
                val scene = previous?.let { WidgetFaceEffects.room(it, snapshot, policy) }
                playFace(context, id, R.layout.widget_wayfinder, R.id.widget_wayfinder_overlay, snapshot.identity,
                    policy, scene, 180, 110)
            }
        }
    }

    fun pushWeek(context: Context, snapshot: WeekWidgetSnapshot, policy: WidgetMotionPolicy = WidgetMotionPolicy.Disabled) {
        prepareMotion(context, policy)
        val mgr = AppWidgetManager.getInstance(context)
        mgr.getAppWidgetIds(ComponentName(context, WeekWidgetProvider::class.java)).forEach { id ->
            val previous = weekFaces.previous(id, snapshot.identity)
            WidgetMotionPlayer.publishFinal(context, id, R.layout.widget_week, R.id.widget_week_overlay, snapshot.identity, policy,
                WidgetExtraViews.week(context, snapshot, id), snapshot.cleared)
            if (snapshot.cleared) weekFaces.forget(id) else {
                weekFaces.remember(id, snapshot.identity, snapshot)
                val scene = previous?.let { WidgetFaceEffects.week(it, snapshot, policy) }
                playFace(context, id, R.layout.widget_week, R.id.widget_week_overlay, snapshot.identity,
                    policy, scene, 250, 170)
            }
        }
    }

    private fun playFace(context: Context, id: Int, layout: Int, overlay: Int, identity: WidgetJobIdentity,
                         policy: WidgetMotionPolicy, scene: WidgetFaceScene?, defaultWidth: Int, defaultHeight: Int) {
        if (scene == null) return
        val options = AppWidgetManager.getInstance(context).getAppWidgetOptions(id)
        val width = options.getInt(AppWidgetManager.OPTION_APPWIDGET_MIN_WIDTH, defaultWidth).takeIf { it > 0 } ?: defaultWidth
        val height = options.getInt(AppWidgetManager.OPTION_APPWIDGET_MIN_HEIGHT, defaultHeight).takeIf { it > 0 } ?: defaultHeight
        WidgetMotionPlayer.play(context, id, layout, overlay, identity, policy) {
            scene.bitmapAt(context, id, width, height, it)
        }
    }

    private val timerFaces = HashMap<Int, String>()

    fun dropTimerFaces() {
        timerFaces.clear()
        timerSnapshots.clear()
    }

    fun pushTimer(context: Context, snapshot: TimerWidgetSnapshot, policy: WidgetMotionPolicy = WidgetMotionPolicy.Disabled) {
        prepareMotion(context, policy)
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
            val previous = timerSnapshots.previous(id, snapshot.identity)
            if (timerFaces[id] == face) {
                // Do not cancel/restart a phase scene, and do not paint a static ring over its frame.
                WidgetMotionPlayer.refreshFinal(id, snapshot.identity, timer(context, snapshot, dp))
                mgr.partiallyUpdateAppWidget(id, moving(context, snapshot, dp, !WidgetMotionPlayer.isRunning(id)))
            } else {
                WidgetMotionPlayer.publishFinal(context, id, R.layout.widget_timer, R.id.widget_timer_overlay, snapshot.identity, policy,
                    timer(context, snapshot, dp), snapshot.cleared)
                timerFaces[id] = face
                val scene = previous?.let { WidgetFaceEffects.timer(it, snapshot, policy) }
                playFace(context, id, R.layout.widget_timer, R.id.widget_timer_overlay, snapshot.identity,
                    policy, scene, dp, 110)
            }
            if (snapshot.cleared) timerSnapshots.forget(id) else timerSnapshots.remember(id, snapshot.identity, snapshot)
        }
    }

    private fun timerFace(snapshot: TimerWidgetSnapshot, widthDp: Int): String =
        listOf(snapshot.identity, snapshot.kind, snapshot.endsAt, snapshot.phaseText, snapshot.subject, snapshot.detail, snapshot.cleared, snapshot.isDark, widthDp)
            .joinToString("|")

    private fun ring(context: Context, snapshot: TimerWidgetSnapshot, widthDp: Int): RemoteViews {
        val views = RemoteViews(context.packageName, R.layout.widget_timer)
        val colors = WidgetPalette.of(context, snapshot.isDark)
        val arc = if (snapshot.kind == TimerPhaseKind.Break) colors.warn else colors.ok
        val fraction = if (snapshot.cleared || snapshot.endsAt?.isAfter(LocalDateTime.now()) != true) 0f else snapshot.fraction
        views.setImageViewBitmap(R.id.widget_timer_ring, TimerRing.bitmap(context, widthDp, fraction, colors.text3, arc))
        return views
    }

    private fun moving(context: Context, snapshot: TimerWidgetSnapshot, widthDp: Int, includeRing: Boolean): RemoteViews {
        val views = if (includeRing) ring(context, snapshot, widthDp) else RemoteViews(context.packageName, R.layout.widget_timer)
        bindClock(views, snapshot, WidgetPalette.of(context, snapshot.isDark), widthDp)
        bindTimerDescription(context, views, snapshot)
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
    val onAccent: Int,
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
                onAccent = color(R.color.widget_light_canvas, R.color.widget_dark_canvas),
                warn = color(R.color.widget_light_warn, R.color.widget_dark_warn),
                bad = color(R.color.widget_light_bad, R.color.widget_dark_bad),
                ok = color(R.color.widget_light_ok, R.color.widget_dark_ok)
            )
        }
    }
}
