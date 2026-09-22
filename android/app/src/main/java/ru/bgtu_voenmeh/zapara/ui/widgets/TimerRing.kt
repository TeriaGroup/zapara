package ru.bgtu_voenmeh.zapara.ui.widgets

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF

object TimerRing {
    fun bitmap(context: Context, sizeDp: Int, fraction: Float, track: Int, arc: Int): Bitmap {
        val density = context.resources.displayMetrics.density.coerceAtLeast(1f)
        val px = (sizeDp.coerceIn(80, 240) * density).toInt().coerceIn(160, 360)
        val bitmap = Bitmap.createBitmap(px, px, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)
        val stroke = px * 0.08f
        val inset = stroke / 2f + 2f
        val oval = RectF(inset, inset, px - inset, px - inset)
        val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeWidth = stroke
            strokeCap = Paint.Cap.BUTT
        }
        paint.color = track
        canvas.drawArc(oval, 0f, 360f, false, paint)
        val left = fraction.coerceIn(0f, 1f)
        if (left > 0f) {
            paint.color = arc
            paint.strokeCap = if (left >= 0.999f) Paint.Cap.BUTT else Paint.Cap.ROUND
            canvas.drawArc(oval, -90f, left * 360f, false, paint)
        }
        return bitmap
    }
}
