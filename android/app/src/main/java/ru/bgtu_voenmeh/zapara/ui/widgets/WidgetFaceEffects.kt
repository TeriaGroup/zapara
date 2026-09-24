package ru.bgtu_voenmeh.zapara.ui.widgets

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Rect
import android.graphics.RectF
import android.view.View
import android.view.ViewGroup
import android.widget.Chronometer
import android.widget.FrameLayout
import android.widget.RemoteViews
import android.widget.TextView
import ru.bgtu_voenmeh.zapara.R
import java.time.Duration
import java.time.LocalDateTime
import java.util.Locale
import kotlin.math.roundToInt

fun timerFaceChanged(previous: TimerWidgetSnapshot, current: TimerWidgetSnapshot): Boolean =
    previous.identity != current.identity || previous.kind != current.kind || previous.endsAt != current.endsAt ||
        previous.phaseText != current.phaseText || previous.subject != current.subject || previous.detail != current.detail ||
        previous.cleared != current.cleared || previous.isDark != current.isDark

data class PhaseArcFrame(val fraction: Float, val haloAlpha: Float)
data class WeekMarkerFrame(val oldAlpha: Float, val newAlpha: Float, val offsetXDp: Float)

fun phaseArc(oldFraction: Float, newFraction: Float, progress: Float): PhaseArcFrame {
    val p = progress.takeUnless(Float::isNaN)?.coerceIn(0f, 1f) ?: 1f
    val old = oldFraction.coerceIn(0f, 1f)
    val next = newFraction.coerceIn(0f, 1f)
    val fraction = if (p <= 0.35f) old + (1f - old) * widgetMotionEase(p / 0.35f)
        else 1f + (next - 1f) * widgetMotionEase((p - 0.35f) / 0.65f)
    val halo = if (p <= 0.35f) widgetMotionEase(p / 0.35f) else 1f - widgetMotionEase((p - 0.35f) / 0.65f)
    return PhaseArcFrame(fraction, halo)
}

fun roomReel(progress: Float): WidgetMotionPose = widgetMotionPose(progress)

/** Horizontal distance uses a 56 dp reference cell; renderers scale it to the measured row. */
fun weekMarker(oldDayIndex: Int, newDayIndex: Int, progress: Float): WeekMarkerFrame {
    val p = progress.takeUnless(Float::isNaN)?.coerceIn(0f, 1f) ?: 1f
    if (oldDayIndex / 4 != newDayIndex / 4) return WeekMarkerFrame(
        1f - widgetMotionEase((p * 2).coerceAtMost(1f)),
        widgetMotionEase((p * 2 - 1f).coerceAtLeast(0f)), 0f)
    return WeekMarkerFrame(1f, 0f, (newDayIndex - oldDayIndex) * 56f * widgetMotionEase(p))
}

object WidgetFaceEffects {
    fun timer(old: TimerWidgetSnapshot, next: TimerWidgetSnapshot, policy: WidgetMotionPolicy): WidgetFaceScene? =
        if (policy.enabled && old.identity == next.identity && !old.cleared && !next.cleared &&
            timerFaceChanged(old, next)) WidgetFaceScene.Timer(old, next) else null

    fun room(old: WayfinderWidgetSnapshot, next: WayfinderWidgetSnapshot, policy: WidgetMotionPolicy): WidgetFaceScene.Room? =
        if (policy.enabled && old.identity == next.identity && !old.cleared && !next.cleared &&
            old.isDark == next.isDark && old.room.isNotBlank() && next.room.isNotBlank() && old.room != next.room)
            WidgetFaceScene.Room(old, next) else null

    fun week(old: WeekWidgetSnapshot, next: WeekWidgetSnapshot, policy: WidgetMotionPolicy): WidgetFaceScene.Week? {
        if (!policy.enabled || old.identity != next.identity || old.cleared || next.cleared ||
            old.isDark != next.isDark || old.days.size != 7 || next.days.size != 7) return null
        val counts = next.days.indices.filter { old.days[it].lessonCount != next.days[it].lessonCount }.toSet()
        val from = old.days.indexOfFirst { it.isToday }
        val to = next.days.indexOfFirst { it.isToday }
        return if (counts.isNotEmpty() || from != to) WidgetFaceScene.Week(old, next, counts, from, to) else null
    }
}

/** Only process memory. Every bitmap is decorative; the already-published final text owns accessibility. */
sealed class WidgetFaceScene(val kind: WidgetMotionKind) {
    class Timer(val old: TimerWidgetSnapshot, val next: TimerWidgetSnapshot) : WidgetFaceScene(WidgetMotionKind.Phase)
    class Room(val old: WayfinderWidgetSnapshot, val next: WayfinderWidgetSnapshot) : WidgetFaceScene(WidgetMotionKind.Room) {
        val oldRoom: String get() = old.room
    }
    class Week(val old: WeekWidgetSnapshot, val next: WeekWidgetSnapshot, val changedCountIndices: Set<Int>,
               val from: Int, val to: Int) : WidgetFaceScene(WidgetMotionKind.Day)

    private var measured: FaceCanvas? = null
    private var oldThemeFace: Bitmap? = null

    fun bitmapAt(context: Context, widgetId: Int, widthDp: Int, heightDp: Int, progress: Float): Bitmap {
        if (this is Timer && old.isDark != next.isDark) {
            val source = oldThemeFace ?: FaceCanvas(context, WidgetRemoteViews.timer(context, old, widthDp),
                widthDp, heightDp).full().also { oldThemeFace = it }
            val frame = Bitmap.createBitmap(source.width, source.height, Bitmap.Config.ARGB_8888)
            val themeProgress = progress.coerceIn(0f, 1f)
            val ink = Paint(Paint.ANTI_ALIAS_FLAG).apply {
                alpha = ((1f - widgetMotionEase(themeProgress)) * 255).roundToInt().coerceIn(0, 255)
            }
            Canvas(frame).drawBitmap(source, 0f, 0f, ink)
            return frame
        }
        val face = measured ?: FaceCanvas(context, when (this) {
            is Timer -> WidgetRemoteViews.timer(context, next, widthDp)
            is Room -> WidgetExtraViews.wayfinder(context, next, widgetId)
            is Week -> WidgetExtraViews.week(context, next, widgetId)
        }, widthDp, heightDp).also { measured = it }
        return face.frame(this, progress)
    }
}

/** Measure the same RemoteViews at the host's dp size; no guessed text baselines or grid offsets. */
private class FaceCanvas(private val context: Context, views: RemoteViews, widthDp: Int, heightDp: Int) {
    private val density = context.resources.displayMetrics.density
    private val geometry = widgetRowBitmapGeometry(widthDp, heightDp, density)
    private val root = views.apply(context, FrameLayout(context)) as ViewGroup
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    init {
        root.measure(View.MeasureSpec.makeMeasureSpec((widthDp * density).roundToInt(), View.MeasureSpec.EXACTLY),
            View.MeasureSpec.makeMeasureSpec((heightDp * density).roundToInt(), View.MeasureSpec.EXACTLY))
        root.layout(0, 0, root.measuredWidth, root.measuredHeight)
        // This detached measurement view must not leave a host-style ticker running.
        root.findViewById<Chronometer>(R.id.widget_timer_time)?.stop()
    }
    fun full(): Bitmap {
        val bitmap = Bitmap.createBitmap(geometry.widthPx, geometry.heightPx, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)
        canvas.scale(geometry.widthPx.toFloat() / root.width, geometry.heightPx.toFloat() / root.height)
        root.draw(canvas)
        return bitmap
    }
    private fun bounds(id: Int): RectF {
        val child = root.findViewById<View>(id)
        val rect = Rect()
        child.getDrawingRect(rect)
        root.offsetDescendantRectToMyCoords(child, rect)
        return RectF(rect)
    }
    private fun card(dark: Boolean) = context.getColor(if (dark) R.color.widget_dark_card else R.color.widget_light_card)
    private fun text(canvas: Canvas, id: Int, value: String? = null, dy: Float = 0f,
                     alpha: Float = 1f, color: Int? = null) {
        val view = root.findViewById<TextView>(id)
        val savedText = view.text
        val savedColor = view.currentTextColor
        if (value != null) {
            view.text = value
            view.measure(View.MeasureSpec.makeMeasureSpec(view.width, View.MeasureSpec.EXACTLY),
                View.MeasureSpec.makeMeasureSpec(view.height, View.MeasureSpec.EXACTLY))
            view.layout(view.left, view.top, view.right, view.bottom)
        }
        if (color != null) view.setTextColor(color)
        try {
            val rect = bounds(id)
            if (rect.width() <= 0f || rect.height() <= 0f) return
            val save = canvas.saveLayerAlpha(rect, (alpha * 255).roundToInt().coerceIn(0, 255))
            try {
                canvas.clipRect(rect)
                canvas.translate(rect.left, rect.top + dy * density)
                view.draw(canvas)
            } finally {
                canvas.restoreToCount(save)
            }
        } finally {
            if (value != null) view.text = savedText
            if (color != null) view.setTextColor(savedColor)
        }
    }

    fun frame(scene: WidgetFaceScene, progress: Float): Bitmap {
        val bitmap = Bitmap.createBitmap(geometry.widthPx, geometry.heightPx, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)
        canvas.scale(geometry.widthPx.toFloat() / root.width, geometry.heightPx.toFloat() / root.height)
        when (scene) {
            is WidgetFaceScene.Timer -> timer(canvas, scene, progress)
            is WidgetFaceScene.Room -> room(canvas, scene, progress)
            is WidgetFaceScene.Week -> week(canvas, scene, progress)
        }
        return bitmap
    }

    private fun timer(canvas: Canvas, scene: WidgetFaceScene.Timer, progress: Float) {
        val colors = WidgetPalette.of(context, scene.next.isDark)
        val area = bounds(R.id.widget_timer_ring)
        val size = minOf(area.width(), area.height())
        val rect = RectF(area.centerX() - size / 2, area.centerY() - size / 2,
            area.centerX() + size / 2, area.centerY() + size / 2)
        val arc = phaseArc(scene.old.fraction, scene.next.fraction, progress)
        fun arcTone(kind: TimerPhaseKind): Int = when (kind) {
            TimerPhaseKind.Lesson -> colors.ok
            TimerPhaseKind.Break -> colors.warn
            else -> colors.text3
        }
        val oldColor = arcTone(scene.old.kind)
        val newColor = arcTone(scene.next.kind)
        TimerRing.draw(canvas, rect, arc.fraction, colors.text3,
            TimerRing.blend(oldColor, newColor, widgetMotionEase(progress)), card(scene.next.isDark), arc.haloAlpha)

        // Mask the final host text for this short scene; the live Chronometer resumes afterward.
        val pose = widgetMotionPose(progress)
        fun reel(id: Int, oldText: String, newText: String, oldInk: Int, newInk: Int) {
            val box = bounds(id)
            if (box.width() <= 0f || box.height() <= 0f) return
            paint.color = card(scene.next.isDark)
            paint.alpha = 255
            box.inset(-density, -density)
            canvas.drawRect(box, paint)
            text(canvas, id, oldText, pose.oldOffsetYDp, pose.oldAlpha, oldInk)
            text(canvas, id, newText, pose.newOffsetYDp, pose.newAlpha, newInk)
        }
        fun clock(snapshot: TimerWidgetSnapshot): String = snapshot.endsAt?.let {
            timerDigitText(Duration.between(LocalDateTime.now(), it).toMillis())
        } ?: snapshot.timeText
        fun tone(kind: TimerPhaseKind): Int = when (kind) {
            TimerPhaseKind.Lesson -> colors.ok
            TimerPhaseKind.Break -> colors.warn
            else -> colors.text2
        }
        fun endLabel(snapshot: TimerWidgetSnapshot): String = snapshot.endsAt?.let {
            context.getString(R.string.widget_timer_until, String.format(Locale.ROOT, "%02d:%02d", it.hour, it.minute))
        }.orEmpty()
        reel(R.id.widget_timer_time, clock(scene.old), clock(scene.next), colors.text1, colors.text1)
        reel(R.id.widget_timer_fallback, endLabel(scene.old), endLabel(scene.next), colors.text1, colors.text1)
        reel(R.id.widget_timer_phase, scene.old.phaseText, scene.next.phaseText,
            tone(scene.old.kind), tone(scene.next.kind))
        reel(R.id.widget_timer_subject, scene.old.subject, scene.next.subject, colors.text1, colors.text1)
        reel(R.id.widget_timer_detail, scene.old.detail, scene.next.detail, colors.text2, colors.text2)
    }

    private fun room(canvas: Canvas, scene: WidgetFaceScene.Room, progress: Float) {
        val pose = roomReel(progress)
        val area = bounds(R.id.widget_wayfinder_room)
        // The final TextView remains accessible underneath. Occlude only its measured room
        // region so the incoming number can move independently from the departing one.
        paint.color = card(scene.next.isDark)
        paint.alpha = 255
        canvas.drawRect(area, paint)
        text(canvas, R.id.widget_wayfinder_room, scene.oldRoom, pose.oldOffsetYDp, pose.oldAlpha)
        text(canvas, R.id.widget_wayfinder_room, scene.next.room, pose.newOffsetYDp, pose.newAlpha)
        paint.color = WidgetPalette.of(context, scene.next.isDark).text1
        paint.alpha = ((1f - progress) * 180).roundToInt().coerceIn(0, 255)
        paint.strokeWidth = density
        canvas.drawLine(area.left, area.bottom - density, area.left + area.width() * widgetMotionEase(progress),
            area.bottom - density, paint)
    }

    private fun week(canvas: Canvas, scene: WidgetFaceScene.Week, progress: Float) {
        val ids = WidgetExtraViews.weekCells
        val colors = WidgetPalette.of(context, scene.next.isDark)
        val moved = scene.from != scene.to
        val affected = scene.changedCountIndices.toMutableSet()
        if (moved) affected.addAll(listOf(scene.from, scene.to).filter { it in ids.indices })
        // The marker can cross intermediate cells in a row; redraw their final text above it.
        if (moved && scene.from >= 0 && scene.to >= 0 && scene.from / 4 == scene.to / 4)
            affected.addAll(minOf(scene.from, scene.to)..maxOf(scene.from, scene.to))
        affected.forEach { index ->
            paint.color = card(scene.next.isDark)
            // RemoteViews scales a <=640 px bitmap. Cover the sampling fringe too, otherwise
            // bilinear filtering exposes a thin outline of the static final marker underneath.
            canvas.drawRect(bounds(ids[index]).apply { inset(-2 * density, -2 * density) }, paint)
            root.findViewById<TextView>(ids[index]).background = null
        }
        fun marker(index: Int, alpha: Float, dx: Float = 0f) {
            if (index !in ids.indices || alpha <= 0f) return
            val rect = bounds(ids[index]).apply { offset(dx, 0f) }
            paint.color = colors.text1
            paint.alpha = (alpha * 255).roundToInt()
            canvas.drawRoundRect(rect, 8 * density, 8 * density, paint)
        }
        val motion = weekMarker(scene.from, scene.to, progress)
        if (moved) {
            val dx = if (scene.from in ids.indices && scene.to in ids.indices && scene.from / 4 == scene.to / 4)
                (bounds(ids[scene.to]).left - bounds(ids[scene.from]).left) * motion.offsetXDp / ((scene.to - scene.from) * 56f) else 0f
            marker(scene.from, motion.oldAlpha, dx)
            marker(scene.to, motion.newAlpha)
        } else if (scene.to in affected) marker(scene.to, 1f)
        affected.forEach { index ->
            val view = root.findViewById<TextView>(ids[index])
            val day = scene.next.days[index]
            val highlighted = if (!moved) day.isToday else if (scene.from / 4 == scene.to / 4)
                index == (if (widgetMotionEase(progress) < 0.5f) scene.from else scene.to)
                else (index == scene.from && motion.oldAlpha > 0.5f) || (index == scene.to && motion.newAlpha > 0.5f)
            view.setTextColor(if (highlighted) colors.onAccent else if (day.lessonCount == 0) colors.text2 else colors.text1)
            if (index !in scene.changedCountIndices) text(canvas, ids[index]) else {
                val rect = bounds(ids[index])
                val split = rect.top + view.totalPaddingTop + (view.layout?.getLineTop(1) ?: (view.height / 2))
                var save = canvas.save()
                canvas.clipRect(rect.left, rect.top, rect.right, split)
                text(canvas, ids[index])
                canvas.restoreToCount(save)
                save = canvas.save()
                canvas.clipRect(rect.left, split, rect.right, rect.bottom)
                val pose = widgetMotionPose(progress)
                val oldCount = scene.old.days[index].lessonCount
                val oldText = "${day.shortName} ${day.date.dayOfMonth}\n" +
                    context.resources.getQuantityString(R.plurals.widget_week_pairs, oldCount, oldCount)
                text(canvas, ids[index], oldText, pose.oldOffsetYDp, pose.oldAlpha)
                text(canvas, ids[index], dy = pose.newOffsetYDp, alpha = pose.newAlpha)
                canvas.restoreToCount(save)
            }
        }
    }
}
