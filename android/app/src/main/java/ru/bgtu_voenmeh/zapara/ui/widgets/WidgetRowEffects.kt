package ru.bgtu_voenmeh.zapara.ui.widgets

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.util.TypedValue
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
    private val departed: String,
    private val departedIndex: Int,
    private val arriving: List<String>,
    private val durationMs: Long,
    private val dark: Boolean,
    private val compact: Boolean = false,
    private val widthDp: Int = 180,
    private val heightDp: Int = 160
) {
    val accentCount: Int get() = arriving.size

    fun sized(widthDp: Int, heightDp: Int): WidgetMotionScene = WidgetMotionScene(
        kind, departed, departedIndex, arriving, durationMs, dark, heightDp < 120,
        widthDp, heightDp
    )

    fun poseAt(progress: Float): WidgetMotionPose = widgetMotionPose(progress)

    fun accentAlphaAt(progress: Float): Float {
        if (kind != WidgetMotionKind.HomeworkCompleted) return 0f
        val eased = widgetMotionEase(progress)
        return (if (eased < 0.3f) eased / 0.3f else (1f - eased) / 0.7f).coerceIn(0f, 1f)
    }

    fun rowAlphaAt(progress: Float, index: Int): Float {
        if (index !in arriving.indices) return 0f
        val delay = (index + 1) * 40f / durationMs.coerceAtLeast(1)
        return widgetMotionEase(((progress - delay) / (1f - delay)).coerceIn(0f, 1f))
    }

    fun bitmapAt(context: Context, progress: Float): Bitmap {
        val metrics = context.resources.displayMetrics
        val geometry = widgetRowBitmapGeometry(widthDp, heightDp, metrics.density)
        val image = Bitmap.createBitmap(geometry.widthPx, geometry.heightPx, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(image)
        canvas.scale(geometry.pixelsPerDp, geometry.pixelsPerDp)
        val paint = Paint(Paint.ANTI_ALIAS_FLAG)
        val top = (if (compact) 40f else 64f) + departedIndex * 44f
        val pose = poseAt(progress)
        val color = context.getColor(if (dark) R.color.widget_dark_text1 else R.color.widget_light_text1)
        paint.color = color
        paint.alpha = (pose.oldAlpha * 230).roundToInt().coerceIn(0, 255)
        paint.textSize = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, 15f, metrics) / metrics.density
        paint.typeface = context.resources.getFont(R.font.inter_medium)
        canvas.drawText(departed.take(32), 12f, top + pose.oldOffsetYDp, paint)
        if (kind == WidgetMotionKind.HomeworkCompleted) {
            paint.color = context.getColor(if (dark) R.color.widget_dark_ok else R.color.widget_light_ok)
            paint.alpha = (accentAlphaAt(progress) * 255).roundToInt()
            paint.style = Paint.Style.STROKE
            paint.strokeWidth = 2f
            val x = widthDp - 30f
            val y = top - 4f - 3f * widgetMotionEase(progress)
            val path = android.graphics.Path().apply {
                moveTo(x - 5f, y)
                lineTo(x - 1f, y + 4f)
                lineTo(x + 6f, y - 5f)
            }
            canvas.drawPath(path, paint)
            paint.style = Paint.Style.FILL
            canvas.drawCircle(x - 12f, y - 5f, 1.5f, paint)
            canvas.drawCircle(x + 11f, y + 4f, 1.5f, paint)
        }
        if (arriving.isNotEmpty()) {
            paint.color = color
            arriving.forEachIndexed { index, title ->
                val arrival = rowAlphaAt(progress, index)
                paint.alpha = (arrival * (1f - progress) * 100).roundToInt()
                val y = top + index * 44f + (1f - arrival) * 12f
                if (y in 0f..heightDp.toFloat()) canvas.drawText(title.take(30), 12f, y, paint)
            }
        }
        return image
    }
}

object WidgetRowEffects {
    fun schedule(previous: ScheduleWidgetSnapshot, current: ScheduleWidgetSnapshot, policy: WidgetMotionPolicy): WidgetMotionScene? {
        if (!policy.enabled || previous.identity != current.identity || previous.cleared || current.cleared) return null
        val ended = current.toss ?: return null
        val index = previous.rows.indexOfFirst { it.faceKey() == ended.faceKey() }
        if (index < 0 || current.rows.any { it.faceKey() == ended.faceKey() }) return null
        return WidgetMotionScene(WidgetMotionKind.ScheduleShift, ended.name, index,
            current.rows.drop(index).take(3).map { it.name }, policy.durationMs, current.isDark)
    }

    fun homework(previous: HomeworkWidgetSnapshot, current: HomeworkWidgetSnapshot,
                 doneIds: Set<Long>, policy: WidgetMotionPolicy): WidgetMotionScene? {
        if (!policy.enabled || previous.identity != current.identity || previous.cleared || current.cleared) return null
        val missing = previous.rows.withIndex().firstOrNull { (_, row) ->
            row.id != 0L && current.rows.none { it.id == row.id }
        }
        val changed = missing ?: previous.rows.withIndex().firstOrNull { (index, row) ->
            row.id != 0L && current.rows.getOrNull(index)?.id != row.id
        } ?: return null
        val completed = missing != null && missing.value.id in doneIds
        return WidgetMotionScene(if (completed) WidgetMotionKind.HomeworkCompleted else WidgetMotionKind.HomeworkShift,
            changed.value.subject, changed.index, current.rows.drop(changed.index).take(1).map { it.subject },
            policy.durationMs, current.isDark)
    }
}
