package ru.bgtu_voenmeh.zapara.ui.gestures

import kotlin.math.abs

internal enum class PlannerSwipeDirection(val dayDelta: Long) {
    Previous(-1), Next(1);

    fun weekIndex(parity: Int): Int? = when {
        this == Next && parity == 1 -> 1
        this == Previous && parity == 2 -> 0
        else -> null
    }
}

internal class PlannerSwipePolicy(
    private val startX: Float,
    private val startY: Float,
    width: Float,
    private val touchSlop: Float,
    private val minimumDistance: Float,
    edgeWidth: Float
) {
    private var rejected = startX < edgeWidth || startX > width - edgeWidth
    private var claimed = false
    private var dx = 0f
    private var dy = 0f

    /** Capture only deliberate horizontal intent; rejecting a scroll is permanent. */
    fun move(x: Float, y: Float, pointerCount: Int = 1, consumed: Boolean = false): Boolean {
        if (pointerCount != 1 || consumed) cancel()
        if (rejected) return false
        dx = x - startX
        dy = y - startY
        if (!claimed && maxOf(abs(dx), abs(dy)) > touchSlop) {
            if (abs(dx) > abs(dy) * 1.5f) claimed = true else cancel()
        } else if (claimed && abs(dy) > abs(dx) && abs(dy) > touchSlop) {
            cancel()
        }
        return claimed && !rejected
    }

    fun cancel() { rejected = true }

    fun finish(): PlannerSwipeDirection? {
        val direction = if (!rejected && claimed && abs(dx) >= minimumDistance && abs(dx) > abs(dy) * 1.5f) {
            if (dx < 0f) PlannerSwipeDirection.Next else PlannerSwipeDirection.Previous
        } else null
        cancel()
        return direction
    }
}
