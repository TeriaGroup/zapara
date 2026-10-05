package ru.bgtu_voenmeh.zapara.ui.widgets

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import kotlin.math.cos
import kotlin.math.sin

object TimerRing {
    fun bitmap(context: Context, sizeDp: Int, fraction: Float, track: Int, arc: Int): Bitmap {
        val density = context.resources.displayMetrics.density.coerceAtLeast(1f)
        val px = (sizeDp.coerceIn(80, 240) * density).toInt().coerceIn(160, 360)
        val bitmap = Bitmap.createBitmap(px, px, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)
        draw(canvas, RectF(0f, 0f, px.toFloat(), px.toFloat()), fraction, track, arc)
        return bitmap
    }

    internal fun blend(old: Int, next: Int, progress: Float): Int = android.graphics.Color.argb(
        255,
        (android.graphics.Color.red(old) + (android.graphics.Color.red(next) - android.graphics.Color.red(old)) * progress).toInt(),
        (android.graphics.Color.green(old) + (android.graphics.Color.green(next) - android.graphics.Color.green(old)) * progress).toInt(),
        (android.graphics.Color.blue(old) + (android.graphics.Color.blue(next) - android.graphics.Color.blue(old)) * progress).toInt())

    internal fun draw(canvas: Canvas, area: RectF, fraction: Float, track: Int, arc: Int,
                      mask: Int? = null, haloAlpha: Float = 0f) {
        val size = minOf(area.width(), area.height())
        val stroke = size * 0.08f
        val oval = RectF(area).apply { inset(size * 0.10f, size * 0.10f) }
        val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeWidth = stroke
            strokeCap = Paint.Cap.BUTT
        }
        if (mask != null) {
            paint.color = mask
            paint.strokeWidth = stroke + size * 0.015f
            canvas.drawOval(oval, paint)
            paint.strokeWidth = stroke
        }
        // Four quarter marks make the remaining share readable in both themes.
        paint.color = track
        paint.alpha = 112
        canvas.drawArc(oval, 0f, 360f, false, paint)
        paint.alpha = 170
        paint.strokeWidth = stroke * 0.18f
        val inner = oval.width() / 2f + stroke * 0.64f
        val outer = oval.width() / 2f + stroke * 1.02f
        if (mask == null) repeat(4) { quarter ->
            val radians = Math.toRadians((quarter * 90 - 90).toDouble())
            val dx = cos(radians).toFloat()
            val dy = sin(radians).toFloat()
            canvas.drawLine(oval.centerX() + dx * inner, oval.centerY() + dy * inner,
                oval.centerX() + dx * outer, oval.centerY() + dy * outer, paint)
        }
        val left = fraction.coerceIn(0f, 1f)
        if (left > 0f) {
            paint.color = arc
            paint.alpha = 255
            paint.strokeWidth = stroke
            paint.strokeCap = if (left >= 0.999f) Paint.Cap.BUTT else Paint.Cap.ROUND
            canvas.drawArc(oval, -90f, left * 360f, false, paint)
        }
        if (haloAlpha > 0f) {
            paint.color = arc
            paint.alpha = (haloAlpha * 150).toInt().coerceIn(0, 255)
            paint.strokeWidth = size * 0.009f
            val halo = RectF(area).apply { inset(size * 0.035f, size * 0.035f) }
            canvas.drawOval(halo, paint)
        }
    }
}
