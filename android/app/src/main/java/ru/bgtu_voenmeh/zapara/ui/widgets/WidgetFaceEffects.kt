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
import ru.bgtu_voenmeh.zapara.R
import kotlin.math.roundToInt
import kotlin.math.cos
import kotlin.math.sin

fun timerFaceChanged(previous: TimerWidgetSnapshot, current: TimerWidgetSnapshot): Boolean =
    previous.identity != current.identity || previous.kind != current.kind || previous.endsAt != current.endsAt ||
        previous.phaseText != current.phaseText || previous.subject != current.subject || previous.detail != current.detail ||
        previous.cleared != current.cleared || previous.isDark != current.isDark

data class PhaseArcFrame(val fraction: Float, val haloAlpha: Float)
data class WeekMarkerFrame(val oldAlpha: Float, val newAlpha: Float, val offsetXDp: Float)
data class PhaseCometFrame(val angleDegrees: Float, val alpha: Float)

/** One lap, including a transparent start and finish; never driven by the countdown heartbeat. */
fun phaseComet(progress: Float): PhaseCometFrame {
    val p = progress.takeUnless(Float::isNaN)?.coerceIn(0f, 1f) ?: 1f
    val alpha = if (p == 0f || p == 1f) 0f else sin(Math.PI * p).toFloat().let { it * it }
    return PhaseCometFrame(-90f + 360f * widgetMotionEase(p), alpha)
}

fun weekMarkerInset(progress: Float): Float {
    val p = progress.takeUnless(Float::isNaN)?.coerceIn(0f, 1f) ?: 1f
    return 0.06f * 4f * p * (1f - p)
}

fun weekCountPose(progress: Float, order: Int): WidgetMotionPose {
    val p = progress.takeUnless(Float::isNaN)?.coerceIn(0f, 1f) ?: 1f
    val delay = order.coerceIn(0, 6) * 0.045f
    return widgetMotionPose(((p - delay) / (1f - delay)).coerceIn(0f, 1f))
}

fun phaseArc(oldFraction: Float, newFraction: Float, progress: Float): PhaseArcFrame {
    val p = progress.takeUnless(Float::isNaN)?.coerceIn(0f, 1f) ?: 1f
    val old = oldFraction.takeUnless(Float::isNaN)?.coerceIn(0f, 1f) ?: 0f
    val next = newFraction.takeUnless(Float::isNaN)?.coerceIn(0f, 1f) ?: 0f
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
            old.readError == null && next.readError == null &&
            timerFaceChanged(old, next)) WidgetFaceScene.Timer(old, next) else null

    fun room(old: WayfinderWidgetSnapshot, next: WayfinderWidgetSnapshot, policy: WidgetMotionPolicy): WidgetFaceScene.Room? =
        if (policy.enabled && old.identity == next.identity && !old.cleared && !next.cleared &&
            old.readError == null && next.readError == null &&
            old.isDark == next.isDark && old.room.isNotBlank() && next.room.isNotBlank() &&
            (old.room != next.room || old.subject != next.subject || old.time != next.time ||
                old.status != next.status || old.targetDate != next.targetDate))
            WidgetFaceScene.Room(old, next) else null

    fun week(old: WeekWidgetSnapshot, next: WeekWidgetSnapshot, policy: WidgetMotionPolicy): WidgetFaceScene.Week? {
        if (!policy.enabled || old.identity != next.identity || old.cleared || next.cleared ||
            old.readError != null || next.readError != null ||
            old.isDark != next.isDark || old.days.size != 7 || next.days.size != 7 ||
            !old.empty.isNullOrBlank() || !next.empty.isNullOrBlank()) return null
        val counts = next.days.indices.filter { old.days[it].lessonCount != next.days[it].lessonCount }.toSet()
        val details = next.days.indices.filter {
            old.days[it].timeSpan != next.days[it].timeSpan || old.days[it].date != next.days[it].date ||
                old.days[it].shortName != next.days[it].shortName
        }.toSet()
        val from = old.days.indexOfFirst { it.isToday }
        val to = next.days.indexOfFirst { it.isToday }
        return if (counts.isNotEmpty() || details.isNotEmpty() || from != to)
            WidgetFaceScene.Week(old, next, counts, from, to, details) else null
    }
}

/** Only process memory. Every bitmap is decorative; the already-published final text owns accessibility. */
sealed class WidgetFaceScene(val kind: WidgetMotionKind) {
    class Timer(val old: TimerWidgetSnapshot, val next: TimerWidgetSnapshot) : WidgetFaceScene(WidgetMotionKind.Phase)
    class Room(val old: WayfinderWidgetSnapshot, val next: WayfinderWidgetSnapshot) : WidgetFaceScene(WidgetMotionKind.Room) {
        val oldRoom: String get() = old.room
        val direction: Int = roomReelDirection(old.room, next.room)
    }
    class Week(val old: WeekWidgetSnapshot, val next: WeekWidgetSnapshot, val changedCountIndices: Set<Int>,
               val from: Int, val to: Int, val changedDetailIndices: Set<Int> = emptySet()) : WidgetFaceScene(WidgetMotionKind.Day)

    private var measured: FaceCanvas? = null

    fun bitmapAt(context: Context, widgetId: Int, widthDp: Int, heightDp: Int, progress: Float): Bitmap {
        val face = measured ?: FaceCanvas(context, when (this) {
            is Timer -> WidgetRemoteViews.timer(context, next, widthDp, heightDp = heightDp)
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
    private val positions = mutableMapOf<Int, RectF>()

    init {
        root.measure(View.MeasureSpec.makeMeasureSpec((widthDp * density).roundToInt(), View.MeasureSpec.EXACTLY),
            View.MeasureSpec.makeMeasureSpec((heightDp * density).roundToInt(), View.MeasureSpec.EXACTLY))
        root.layout(0, 0, root.measuredWidth, root.measuredHeight)
        root.findViewById<Chronometer>(R.id.widget_timer_time)?.stop()
    }

    private fun bounds(id: Int): RectF {
        positions[id]?.let { return RectF(it) }
        val child = root.findViewById<View>(id)
        if (child.visibility != View.VISIBLE) return RectF()
        val rect = Rect()
        child.getDrawingRect(rect)
        root.offsetDescendantRectToMyCoords(child, rect)
        return RectF(rect).also { positions[id] = RectF(it) }
    }

    fun frame(scene: WidgetFaceScene, progress: Float): Bitmap {
        val bitmap = Bitmap.createBitmap(geometry.widthPx, geometry.heightPx, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)
        canvas.scale(geometry.widthPx.toFloat() / root.width, geometry.heightPx.toFloat() / root.height)
        val p = progress.takeUnless(Float::isNaN)?.coerceIn(0f, 1f) ?: 1f
        // Finishing is an exact transparent overlay over the already-published native face.
        if (p == 1f) return bitmap
        when (scene) {
            is WidgetFaceScene.Timer -> timer(canvas, scene, p)
            is WidgetFaceScene.Room -> room(canvas, scene, p)
            is WidgetFaceScene.Week -> week(canvas, scene, p)
        }
        return bitmap
    }

    private fun timer(canvas: Canvas, scene: WidgetFaceScene.Timer, progress: Float) {
        val colors = WidgetPalette.of(context, scene.next.isDark)
        val area = bounds(R.id.widget_timer_ring)
        val size = minOf(area.width(), area.height())
        if (size <= 0f) return
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
        // Never cover the native Chronometer or phase. Only the ring's annulus is decorative.
        val annulus = android.graphics.Path().apply {
            fillType = android.graphics.Path.FillType.EVEN_ODD
            addCircle(rect.centerX(), rect.centerY(), size * 0.5f, android.graphics.Path.Direction.CW)
            addCircle(rect.centerX(), rect.centerY(), size * 0.345f, android.graphics.Path.Direction.CW)
        }
        val save = canvas.save()
        canvas.clipPath(annulus)
        TimerRing.draw(canvas, rect, arc.fraction, colors.text3,
            TimerRing.blend(oldColor, newColor, widgetMotionEase(progress)),
            context.getColor(if (scene.next.isDark) R.color.widget_dark_card else R.color.widget_light_card), arc.haloAlpha)
        if (scene.old.kind != scene.next.kind || scene.old.endsAt != scene.next.endsAt) {
            val comet = phaseComet(progress)
            if (comet.alpha > 0f) {
                val oval = RectF(rect).apply { inset(size * 0.10f, size * 0.10f) }
                paint.style = Paint.Style.STROKE
                paint.strokeCap = Paint.Cap.ROUND
                paint.strokeWidth = size * 0.035f
                paint.color = TimerRing.blend(newColor, colors.text1, 0.65f)
                repeat(5) { segment ->
                    paint.alpha = (comet.alpha * (5 - segment) * 32).roundToInt()
                    canvas.drawArc(oval, comet.angleDegrees - (segment + 1) * 6f, 5f, false, paint)
                }
                paint.style = Paint.Style.FILL
                paint.alpha = (comet.alpha * 230).roundToInt()
                val radians = Math.toRadians(comet.angleDegrees.toDouble())
                canvas.drawCircle(oval.centerX() + oval.width() / 2f * cos(radians).toFloat(),
                    oval.centerY() + oval.height() / 2f * sin(radians).toFloat(), size * 0.028f, paint)
            }
        }
        canvas.restoreToCount(save)
    }

    /** A 1 dp sweep below the measured native view; it cannot mask or duplicate its glyphs. */
    private fun edge(canvas: Canvas, id: Int, progress: Float, ink: Int, direction: Int = 1) {
        val area = bounds(id)
        if (area.isEmpty) return
        val y = area.bottom + density
        if (y + density > root.height) return
        val inset = minOf(2 * density, area.width() / 4f)
        val left = area.left + inset
        val width = (area.width() - 2 * inset).coerceAtLeast(0f)
        val sweep = widgetRowSweep(widgetMotionEase(progress), width / density)
        if (sweep.alpha <= 0f || sweep.head <= sweep.tail) return
        val tail = if (direction >= 0) left + sweep.tail * density else left + width - sweep.head * density
        val head = if (direction >= 0) left + sweep.head * density else left + width - sweep.tail * density
        paint.style = Paint.Style.STROKE
        paint.strokeCap = Paint.Cap.ROUND
        paint.strokeWidth = density
        paint.color = ink
        paint.alpha = (sweep.alpha * 150).roundToInt()
        canvas.drawLine(tail, y, head, y, paint)
    }

    private fun room(canvas: Canvas, scene: WidgetFaceScene.Room, progress: Float) {
        val colors = WidgetPalette.of(context, scene.next.isDark)
        if (scene.old.room != scene.next.room) {
            edge(canvas, R.id.widget_wayfinder_room, progress, colors.text1, scene.direction)
        } else {
            if (scene.old.subject != scene.next.subject)
                edge(canvas, R.id.widget_wayfinder_subject, progress, colors.text2)
            if (scene.old.time != scene.next.time || scene.old.status != scene.next.status || scene.old.targetDate != scene.next.targetDate)
                edge(canvas, R.id.widget_wayfinder_time, progress, colors.text2)
        }
    }

    private fun week(canvas: Canvas, scene: WidgetFaceScene.Week, progress: Float) {
        val affected = (scene.changedCountIndices + scene.changedDetailIndices).toMutableSet()
        if (scene.from != scene.to && scene.to in WidgetExtraViews.weekCells.indices) affected += scene.to
        val colors = WidgetPalette.of(context, scene.next.isDark)
        affected.sorted().forEachIndexed { order, index ->
            val delay = order.coerceIn(0, 6) * 0.045f
            val local = ((progress - delay) / (1f - delay)).coerceIn(0f, 1f)
            edge(canvas, WidgetExtraViews.weekCells[index], local, colors.text1)
        }
    }
}
