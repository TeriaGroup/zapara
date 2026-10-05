package ru.bgtu_voenmeh.zapara.ui.widgets

import kotlin.math.sin

private fun effectProgress(value: Float): Float = value.takeUnless(Float::isNaN)?.coerceIn(0f, 1f) ?: 1f

/** A numbered destination gives the reel a stable direction; named places use the normal arrival. */
internal fun roomReelDirection(old: String, next: String): Int {
    fun number(value: String) = Regex("\\d+").find(value)?.value?.toIntOrNull()
    val before = number(old)
    val after = number(next)
    return if (before != null && after != null && after < before) -1 else 1
}

/** Translation only: a single sub-dp settle keeps the incoming glyphs at their final scale. */
internal fun directionalRoomReel(progress: Float, direction: Int): WidgetMotionPose {
    val p = effectProgress(progress)
    val pose = widgetMotionPose(p)
    val settleProgress = ((p - 0.45f) / 0.55f).coerceIn(0f, 1f)
    val settle = if (settleProgress == 0f || settleProgress == 1f) 0f
        else 1.2f * sin(Math.PI * settleProgress).toFloat() * (1f - settleProgress)
    val sign = if (direction < 0) -1f else 1f
    return pose.copy(oldOffsetYDp = pose.oldOffsetYDp * sign,
        newOffsetYDp = (pose.newOffsetYDp - settle) * sign)
}

internal data class WidgetEffectRect(val left: Float, val top: Float, val right: Float, val bottom: Float)

/** Same measured path within a row and between rows; never a disappearing/reappearing marker. */
internal fun travellingWeekMarker(from: WidgetEffectRect, to: WidgetEffectRect, progress: Float): WidgetEffectRect {
    val p = effectProgress(progress)
    if (p == 0f) return from
    if (p == 1f) return to
    val eased = widgetMotionEase(p)
    fun mix(a: Float, b: Float) = a + (b - a) * eased
    return WidgetEffectRect(mix(from.left, to.left), mix(from.top, to.top), mix(from.right, to.right), mix(from.bottom, to.bottom))
}

internal data class WidgetRowSweep(val tail: Float, val head: Float, val alpha: Float)

/** Clamp both ends independently so even a narrow row never draws a backwards underline. */
internal fun widgetRowSweep(travel: Float, widthDp: Float): WidgetRowSweep {
    val p = effectProgress(travel)
    val width = widthDp.takeIf(Float::isFinite)?.coerceAtLeast(0f) ?: 0f
    val length = minOf(24f, width * 0.35f)
    val head = (width + length) * p
    return WidgetRowSweep((head - length).coerceIn(0f, width), head.coerceIn(0f, width), 4f * p * (1f - p))
}

internal data class WidgetCompletionCheck(val drawFraction: Float, val alpha: Float, val offsetYDp: Float)

internal fun widgetCompletionCheck(progress: Float): WidgetCompletionCheck {
    val p = effectProgress(progress)
    val draw = widgetMotionEase((p / 0.6f).coerceIn(0f, 1f))
    val fade = widgetMotionEase(((p - 0.5f) / 0.5f).coerceIn(0f, 1f))
    val alpha = widgetMotionEase((p / 0.18f).coerceIn(0f, 1f)) * (1f - fade)
    return WidgetCompletionCheck(draw, alpha, if (p == 0f || p == 1f) 0f else -1.5f * sin(Math.PI * p).toFloat())
}
