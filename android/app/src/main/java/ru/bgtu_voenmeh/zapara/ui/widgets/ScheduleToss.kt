package ru.bgtu_voenmeh.zapara.ui.widgets

import android.appwidget.AppWidgetManager
import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.graphics.Typeface
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import android.text.TextPaint
import android.util.Log
import android.view.View
import android.widget.RemoteViews
import ru.bgtu_voenmeh.zapara.R
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min
import kotlin.math.sin

internal fun scheduleTossFits(widthDp: Int, heightDp: Int): Boolean =
    widthDp >= 180 && heightDp >= 150

internal fun shouldTossSchedule(
    fits: Boolean,
    motionScale: Float,
    savedProfile: String?,
    savedKeys: Set<String>,
    profile: String,
    tossKey: String?
): Boolean {
    if (!fits || motionScale <= 0f || tossKey.isNullOrEmpty()) return false
    if (savedProfile != profile) return false
    return tossKey in savedKeys
}

internal fun schedulePaperStartY(heightDp: Int): Float {
    if (heightDp <= 0) return 0.4f
    val mid = 12f + 22f + 4f + 16f + 8f + 18f
    return (mid / heightDp.toFloat()).coerceIn(0.22f, 0.62f)
}

internal data class TossPose(
    val cx: Float,
    val cy: Float,
    val scale: Float,
    val rotation: Float,
    val crumple: Float,
    val paper: Float,
    val lid: Float,
    val bin: Float
)

internal fun tossPose(t: Float, startY: Float): TossPose {
    val e = scheduleEase(t)
    val crumple = smoothStep(0.02f, 0.5f, e)
    val fly = smoothStep(0.16f, 0.84f, e)
    val lift = 4f * fly * (1f - fly)
    val paper = 1f - smoothStep(0.86f, 1f, e)
    val lid = smoothStep(0.08f, 0.4f, e) * (1f - smoothStep(0.72f, 0.94f, e))
    return TossPose(
        cx = lerp(0.46f, 0.84f, fly),
        cy = lerp(startY, 0.14f, fly) - lift * 0.06f,
        scale = lerp(1f, 0.08f, smoothStep(0.28f, 1f, e)),
        rotation = 18f * fly + 12f * crumple * (if (fly < 0.5f) -1f else 1f),
        crumple = crumple,
        paper = paper.coerceIn(0f, 1f),
        lid = lid.coerceIn(0f, 1f),
        bin = (1f - smoothStep(0.9f, 1f, e)).coerceIn(0f, 1f)
    )
}

internal fun scheduleEase(t: Float): Float {
    val x = t.coerceIn(0f, 1f)
    var lo = 0f
    var hi = 1f
    repeat(16) {
        val mid = (lo + hi) * 0.5f
        if (bezier(mid, 0.2f, 0.2f) < x) lo = mid else hi = mid
    }
    return bezier((lo + hi) * 0.5f, 0.8f, 1f)
}

private fun bezier(t: Float, c1: Float, c2: Float): Float {
    val u = 1f - t
    return 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t
}

private fun smoothStep(edge0: Float, edge1: Float, t: Float): Float {
    if (t <= edge0) return 0f
    if (t >= edge1) return 1f
    val x = (t - edge0) / (edge1 - edge0)
    return x * x * (3f - 2f * x)
}

private fun lerp(a: Float, b: Float, t: Float) = a + (b - a) * t

internal fun scheduleTossBitmap(
    context: Context,
    widthDp: Int,
    heightDp: Int,
    note: ScheduleWidgetRow,
    t: Float
): Bitmap {
    val density = context.resources.displayMetrics.density.coerceAtLeast(1f)
    var width = (widthDp * density).toInt().coerceAtLeast(1)
    var height = (heightDp * density).toInt().coerceAtLeast(1)
    val fit = min(1f, 640f / max(width, height).toFloat())
    width = (width * fit).toInt().coerceAtLeast(1)
    height = (height * fit).toInt().coerceAtLeast(1)
    val px = width / widthDp.toFloat().coerceAtLeast(1f)
    val bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
    val canvas = Canvas(bitmap)
    val pose = tossPose(t, schedulePaperStartY(heightDp))
    val binX = width * 0.84f
    val binY = height * 0.14f
    val bin = 28f * px
    drawBin(canvas, binX, binY, bin, pose, front = false)
    drawPaper(canvas, context, note, pose, width, height, px)
    drawBin(canvas, binX, binY, bin, pose, front = true)
    return bitmap
}

private fun drawBin(canvas: Canvas, cx: Float, cy: Float, size: Float, pose: TossPose, front: Boolean) {
    if (pose.bin <= 0.01f) return
    val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    if (!front) {
        val body = Path().apply {
            moveTo(cx - size * 0.42f, cy - size * 0.02f)
            lineTo(cx + size * 0.42f, cy - size * 0.02f)
            lineTo(cx + size * 0.32f, cy + size * 0.62f)
            lineTo(cx - size * 0.32f, cy + size * 0.62f)
            close()
        }
        paint.style = Paint.Style.FILL
        paint.color = 0xFF1A1A1A.toInt()
        paint.alpha = (pose.bin * 255).toInt()
        canvas.drawPath(body, paint)
        paint.color = 0xFF0C0C0C.toInt()
        paint.alpha = (pose.bin * 220).toInt()
        canvas.drawOval(
            RectF(cx - size * 0.28f, cy - size * 0.08f, cx + size * 0.28f, cy + size * 0.12f),
            paint
        )
        return
    }
    val body = Path().apply {
        moveTo(cx - size * 0.42f, cy - size * 0.02f)
        lineTo(cx + size * 0.42f, cy - size * 0.02f)
        lineTo(cx + size * 0.32f, cy + size * 0.62f)
        lineTo(cx - size * 0.32f, cy + size * 0.62f)
        close()
    }
    paint.style = Paint.Style.STROKE
    paint.strokeWidth = size * 0.06f
    paint.color = 0xFFB0B0B0.toInt()
    paint.alpha = (pose.bin * 255).toInt()
    canvas.drawPath(body, paint)
    canvas.save()
    canvas.rotate(-42f * pose.lid, cx - size * 0.48f, cy - size * 0.16f)
    paint.style = Paint.Style.FILL
    paint.color = 0xFF3A3A3A.toInt()
    paint.alpha = (pose.bin * 255).toInt()
    canvas.drawRoundRect(
        RectF(cx - size * 0.52f, cy - size * 0.28f, cx + size * 0.52f, cy - size * 0.08f),
        size * 0.06f,
        size * 0.06f,
        paint
    )
    canvas.restore()
}

private fun drawPaper(
    canvas: Canvas,
    context: Context,
    note: ScheduleWidgetRow,
    pose: TossPose,
    width: Int,
    height: Int,
    px: Float
) {
    if (pose.paper <= 0.01f || pose.scale <= 0.01f) return
    val paperW = width * 0.74f * pose.scale
    val paperH = (52f * px) * pose.scale * (1f - 0.25f * pose.crumple)
    val left = width * pose.cx - paperW / 2f
    val top = height * pose.cy - paperH / 2f
    val rect = RectF(left, top, left + paperW, top + paperH)
    canvas.save()
    canvas.rotate(pose.rotation, rect.centerX(), rect.centerY())
    val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    if (pose.crumple < 0.85f) {
        paint.color = 0x66000000
        paint.alpha = (pose.paper * 70 * (1f - pose.crumple)).toInt().coerceIn(0, 255)
        canvas.drawRoundRect(
            RectF(rect.left + 3f * px, rect.top + 4f * px, rect.right + 2f * px, rect.bottom + 4f * px),
            8f * px,
            8f * px,
            paint
        )
    }
    val strips = 6
    val slice = rect.height() / strips
    val path = Path()
    for (i in 0 until strips) {
        val wave = sin((i + pose.crumple * 4f) * 1.15f)
        val pinch = pose.crumple * rect.width() * 0.24f * (0.35f + 0.65f * abs(wave))
        val dy = pose.crumple * slice * 0.7f * wave
        val y0 = rect.top + i * slice + dy
        val y1 = y0 + slice
        path.rewind()
        path.moveTo(rect.left + pinch, y0)
        path.lineTo(rect.right - pinch * 0.85f, y0 + dy * 0.15f)
        path.lineTo(rect.right - pinch * 0.45f, y1)
        path.lineTo(rect.left + pinch * 0.35f, y1 - dy * 0.1f)
        path.close()
        paint.style = Paint.Style.FILL
        paint.color = if (i % 2 == 0) 0xFFF7F4EE.toInt() else 0xFFE7E0D4.toInt()
        paint.alpha = (pose.paper * 255).toInt()
        canvas.drawPath(path, paint)
    }
    if (pose.crumple < 0.62f && paperW > 24f * px) {
        val fade = pose.paper * (1f - pose.crumple / 0.62f)
        val name = TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0xFF1C1C1C.toInt()
            alpha = (fade * 255).toInt()
            textSize = 14f * px * pose.scale.coerceAtLeast(0.45f)
            typeface = widgetTypeface(context, medium = true)
        }
        val meta = TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0xFF5C564C.toInt()
            alpha = (fade * 255).toInt()
            textSize = 11f * px * pose.scale.coerceAtLeast(0.45f)
            typeface = widgetTypeface(context, medium = false)
        }
        val pad = 8f * px
        val textW = rect.width() - pad * 2f - 18f * px
        canvas.save()
        canvas.clipRect(rect)
        canvas.drawText(fitText(name, note.name, textW), rect.left + pad, rect.top + name.textSize + 2f * px, name)
        if (note.meta.isNotBlank()) {
            canvas.drawText(fitText(meta, note.meta, textW), rect.left + pad, rect.top + name.textSize + meta.textSize + 6f * px, meta)
        }
        if (note.number > 0) {
            name.textAlign = Paint.Align.RIGHT
            canvas.drawText(note.number.toString(), rect.right - pad, rect.centerY() + name.textSize * 0.3f, name)
        }
        canvas.restore()
    }
    canvas.restore()
}

private fun fitText(paint: TextPaint, text: String, max: Float): String {
    if (max <= 0f || paint.measureText(text) <= max) return text
    var end = text.length
    while (end > 0 && paint.measureText(text.substring(0, end) + "...") > max) end--
    return if (end <= 0) "" else text.substring(0, end) + "..."
}

private fun widgetTypeface(context: Context, medium: Boolean): Typeface {
    val id = if (medium) R.font.inter_medium else R.font.inter_regular
    return runCatching { context.resources.getFont(id) }.getOrNull()
        ?: if (medium) Typeface.DEFAULT_BOLD else Typeface.DEFAULT
}

internal object ScheduleWidgetMemory {
    private const val FILE = "schedule_widget_rows"

    fun read(context: Context, id: Int): Pair<String, Set<String>>? {
        val raw = context.getSharedPreferences(FILE, Context.MODE_PRIVATE).getString(id.toString(), null) ?: return null
        val lines = raw.split('\n')
        if (lines.isEmpty()) return null
        return lines[0] to lines.drop(1).filter { it.isNotEmpty() }.toSet()
    }

    fun write(context: Context, id: Int, profile: String, keys: Collection<String>) {
        val raw = buildString {
            append(profile.replace('\n', ' '))
            keys.forEach {
                append('\n')
                append(it.replace('\n', ' '))
            }
        }
        context.getSharedPreferences(FILE, Context.MODE_PRIVATE).edit().putString(id.toString(), raw).apply()
    }
}

internal object ScheduleTossPlayer {
    private const val FRAMES = 10
    private val main = Handler(Looper.getMainLooper())
    private val tokens = HashMap<Int, Int>()

    fun cancel(id: Int) {
        tokens[id] = (tokens[id] ?: 0) + 1
    }

    fun play(context: Context, widgetId: Int, note: ScheduleWidgetRow, widthDp: Int, heightDp: Int) {
        val app = context.applicationContext
        val scale = runCatching {
            Settings.Global.getFloat(app.contentResolver, Settings.Global.ANIMATOR_DURATION_SCALE, 1f)
        }.getOrDefault(1f)
        if (scale <= 0f) return
        val token = (tokens[widgetId] ?: 0) + 1
        tokens[widgetId] = token
        val delay = (64f * scale).toLong().coerceIn(16L, 280L)
        fun step(frame: Int) {
            if (tokens[widgetId] != token) return
            if (frame >= FRAMES) {
                hide(app, widgetId)
                return
            }
            val bitmap = try {
                scheduleTossBitmap(app, widthDp, heightDp, note, frame / (FRAMES - 1f))
            } catch (e: Exception) {
                Log.w("ZaparaWidget", "toss", e)
                hide(app, widgetId)
                return
            }
            val views = RemoteViews(app.packageName, R.layout.widget_schedule)
            views.setViewVisibility(R.id.widget_schedule_toss, View.VISIBLE)
            views.setImageViewBitmap(R.id.widget_schedule_toss, bitmap)
            try {
                AppWidgetManager.getInstance(app).partiallyUpdateAppWidget(widgetId, views)
            } catch (e: Exception) {
                Log.w("ZaparaWidget", "toss", e)
                return
            }
            main.postDelayed({ step(frame + 1) }, delay)
        }
        main.postDelayed({ step(0) }, delay)
    }

    private fun hide(context: Context, widgetId: Int) {
        val views = RemoteViews(context.packageName, R.layout.widget_schedule)
        views.setViewVisibility(R.id.widget_schedule_toss, View.GONE)
        runCatching { AppWidgetManager.getInstance(context).partiallyUpdateAppWidget(widgetId, views) }
    }
}
