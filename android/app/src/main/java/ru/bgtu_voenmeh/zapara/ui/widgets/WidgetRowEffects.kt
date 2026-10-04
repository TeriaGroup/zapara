package ru.bgtu_voenmeh.zapara.ui.widgets

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Rect
import android.graphics.RectF
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.RemoteViews
import ru.bgtu_voenmeh.zapara.R
import kotlin.math.roundToInt

internal data class WidgetRowBitmapGeometry(val widthPx: Int, val heightPx: Int, val pixelsPerDp: Float)

/** One scale for both axes keeps dp positions stable when RemoteViews fits the bitmap to its host. */
internal fun widgetRowBitmapGeometry(widthDp: Int, heightDp: Int, density: Float): WidgetRowBitmapGeometry {
    val width = widthDp.coerceAtLeast(1)
    val height = heightDp.coerceAtLeast(1)
    val physicalScale = density.coerceAtLeast(0.1f)
    val fit = (640f / (maxOf(width, height) * physicalScale)).coerceAtMost(1f)
    val pixelsPerDp = physicalScale * fit
    return WidgetRowBitmapGeometry(
        (width * pixelsPerDp).roundToInt().coerceIn(1, 640),
        (height * pixelsPerDp).roundToInt().coerceIn(1, 640), pixelsPerDp
    )
}

/** A decorative overlay. The final RemoteViews text remains visible and accessible underneath. */
class WidgetMotionScene internal constructor(
    val kind: WidgetMotionKind,
    private val rowIndices: List<Int>,
    private val durationMs: Long,
    private val dark: Boolean,
    private val schedule: ScheduleWidgetSnapshot? = null,
    private val homework: HomeworkWidgetSnapshot? = null,
    private val widthDp: Int = 180,
    private val heightDp: Int = 160
) {
    val accentCount: Int get() = rowIndices.size
    private var measured: List<RectF>? = null
    private var framePaint: Paint? = null

    fun sized(widthDp: Int, heightDp: Int): WidgetMotionScene = WidgetMotionScene(
        kind, rowIndices, durationMs, dark, schedule, homework, widthDp, heightDp
    )

    fun poseAt(progress: Float): WidgetMotionPose = widgetMotionPose(progress)

    fun accentAlphaAt(progress: Float): Float {
        if (kind != WidgetMotionKind.HomeworkCompleted) return 0f
        return widgetCompletionCheck(progress).alpha
    }

    fun rowAlphaAt(progress: Float, index: Int): Float {
        if (index !in rowIndices.indices) return 0f
        val delay = ((index + 1) * 40f / durationMs.coerceAtLeast(1)).coerceAtMost(0.8f)
        return widgetMotionEase(((progress - delay) / (1f - delay)).coerceIn(0f, 1f))
    }

    /** Resolve row containers from the same layout and bindings the launcher receives. */
    private fun measure(context: Context): List<RectF> {
        val views: RemoteViews
        val ids: List<Int>
        val emptyId: Int
        if (schedule != null) {
            views = WidgetRemoteViews.schedule(context, schedule, heightDp)
            ids = listOf(R.id.widget_schedule_row1, R.id.widget_schedule_row2,
                R.id.widget_schedule_row3, R.id.widget_schedule_row4)
            emptyId = R.id.widget_schedule_empty
        } else {
            views = WidgetRemoteViews.homework(context, requireNotNull(homework), heightDp)
            ids = listOf(R.id.widget_homework_row1, R.id.widget_homework_row2,
                R.id.widget_homework_row3, R.id.widget_homework_row4)
            emptyId = R.id.widget_homework_empty
        }
        val density = context.resources.displayMetrics.density
        val root = views.apply(context, FrameLayout(context)) as ViewGroup
        root.measure(View.MeasureSpec.makeMeasureSpec((widthDp * density).roundToInt(), View.MeasureSpec.EXACTLY),
            View.MeasureSpec.makeMeasureSpec((heightDp * density).roundToInt(), View.MeasureSpec.EXACTLY))
        root.layout(0, 0, root.measuredWidth, root.measuredHeight)
        return rowIndices.ifEmpty { listOf(-1) }.map { index ->
            val child = root.findViewById<View>(ids.getOrElse(index) { emptyId })
            if (child.visibility != View.VISIBLE) RectF() else {
                val bounds = Rect()
                child.getDrawingRect(bounds)
                root.offsetDescendantRectToMyCoords(child, bounds)
                RectF(bounds.left / density, bounds.top / density, bounds.right / density, bounds.bottom / density)
            }
        }
    }

    fun bitmapAt(context: Context, progress: Float): Bitmap {
        val metrics = context.resources.displayMetrics
        val geometry = widgetRowBitmapGeometry(widthDp, heightDp, metrics.density)
        val image = Bitmap.createBitmap(geometry.widthPx, geometry.heightPx, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(image)
        canvas.scale(geometry.pixelsPerDp, geometry.pixelsPerDp)
        val paint = framePaint ?: Paint(Paint.ANTI_ALIAS_FLAG).also { framePaint = it }
        val areas = measured ?: measure(context).also { measured = it }
        val colors = WidgetPalette.of(context, dark)
        val p = progress.takeUnless(Float::isNaN)?.coerceIn(0f, 1f) ?: 1f
        areas.forEachIndexed { index, area ->
            if (area.isEmpty) return@forEachIndexed
            val travel = if (rowIndices.isEmpty()) widgetMotionEase(p) else rowAlphaAt(p, index)
            val sweep = widgetRowSweep(travel, area.width())
            val pulse = sweep.alpha
            paint.color = if (kind == WidgetMotionKind.HomeworkCompleted) colors.ok else colors.text1
            // The text stays live: tint at <= 7% opacity, with a moving underline below it.
            paint.alpha = (pulse * 18).roundToInt()
            canvas.drawRoundRect(area, 6f, 6f, paint)
            val save = canvas.save()
            canvas.clipRect(area.left, area.top, area.right, minOf(area.bottom + 2f, heightDp.toFloat()))
            paint.alpha = (pulse * 150).roundToInt()
            paint.strokeWidth = 1.5f
            paint.strokeCap = Paint.Cap.ROUND
            if (sweep.head > sweep.tail) canvas.drawLine(area.left + sweep.tail, area.bottom + 0.5f,
                area.left + sweep.head, area.bottom + 0.5f, paint)
            canvas.restoreToCount(save)
            if (kind == WidgetMotionKind.HomeworkCompleted && index == 0) {
                // Completion feedback sits in the outer padding, never over the next task.
                val check = widgetCompletionCheck(p)
                paint.alpha = (check.alpha * 255).roundToInt()
                paint.style = Paint.Style.STROKE
                paint.strokeJoin = Paint.Join.ROUND
                val x = (area.left - 6f).coerceAtLeast(3f)
                val y = area.centerY() + check.offsetYDp
                val path = android.graphics.Path().apply {
                    moveTo(x - 2f, y)
                    val first = (check.drawFraction / 0.33f).coerceIn(0f, 1f)
                    lineTo(x - 2f + 2f * first, y + 2f * first)
                    if (check.drawFraction > 0.33f) {
                        val second = ((check.drawFraction - 0.33f) / 0.67f).coerceIn(0f, 1f)
                        lineTo(x + 3f * second, y + 2f - 5f * second)
                    }
                }
                canvas.drawPath(path, paint)
                paint.style = Paint.Style.FILL
            }
        }
        return image
    }
}

object WidgetRowEffects {
    fun schedule(previous: ScheduleWidgetSnapshot, current: ScheduleWidgetSnapshot, policy: WidgetMotionPolicy): WidgetMotionScene? {
        if (previous.readError != null || current.readError != null) return null
        if (!policy.enabled || previous.identity != current.identity || previous.cleared || current.cleared) return null
        if (previous.isDark != current.isDark || previous.rows == current.rows) return null
        val ended = current.toss
        val index = ended?.let { row -> previous.rows.indexOfFirst { it.faceKey() == row.faceKey() } } ?: -1
        val shifted = index >= 0 && current.rows.none { it.faceKey() == ended?.faceKey() }
        val changed = if (shifted) current.rows.indices.drop(index).take(3)
            else current.rows.indices.filter { previous.rows.getOrNull(it) != current.rows[it] }.take(4)
        return WidgetMotionScene(if (shifted) WidgetMotionKind.ScheduleShift else WidgetMotionKind.ScheduleUpdated,
            changed, policy.durationMs, current.isDark, schedule = current)
    }

    fun homework(previous: HomeworkWidgetSnapshot, current: HomeworkWidgetSnapshot,
                 doneIds: Set<Long>, policy: WidgetMotionPolicy): WidgetMotionScene? {
        if (previous.readError != null || current.readError != null) return null
        if (!policy.enabled || previous.identity != current.identity || previous.cleared || current.cleared) return null
        if (previous.isDark != current.isDark || previous.rows == current.rows) return null
        val missing = previous.rows.withIndex().firstOrNull { (_, row) ->
            row.id != 0L && current.rows.none { it.id == row.id }
        }
        val reordered = previous.rows.withIndex().firstOrNull { (index, row) ->
            row.id != 0L && current.rows.getOrNull(index)?.id != row.id
        }
        val completed = missing != null && missing.value.id in doneIds
        val kind = when {
            completed -> WidgetMotionKind.HomeworkCompleted
            missing != null || reordered != null -> WidgetMotionKind.HomeworkShift
            else -> WidgetMotionKind.HomeworkUpdated
        }
        val changed = current.rows.indices.filter { previous.rows.getOrNull(it) != current.rows[it] }.take(4)
            .ifEmpty { current.rows.indices.toList().takeLast(1) }
        return WidgetMotionScene(kind, changed, policy.durationMs, current.isDark, homework = current)
    }
}
