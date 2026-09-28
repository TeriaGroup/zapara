package ru.bgtu_voenmeh.zapara.ui.gestures

import kotlin.math.abs

/** One pointer sequence, measured from its original down position. */
internal class SheetDragPolicy(private val touchSlop: Float, private val dismissDistance: Float) {
    private var cancelled = false
    private var finished = false
    private var dragging = false
    private var x = 0f
    private var y = 0f

    fun move(totalX: Float, totalY: Float, pointerCount: Int = 1): Boolean {
        if (cancelled || finished) return false
        if (pointerCount != 1 || !totalX.isFinite() || !totalY.isFinite()) {
            cancel()
            return false
        }
        x = totalX
        y = totalY
        if (!dragging) {
            if (y < -touchSlop || (abs(x) > touchSlop && abs(x) >= y)) {
                cancel()
                return false
            }
            dragging = y > touchSlop && y > abs(x) * 1.25f
        }
        return dragging
    }

    fun cancel() { cancelled = true }

    fun finish(): Boolean {
        if (finished) return false
        finished = true
        return !cancelled && dragging && y >= dismissDistance && y > abs(x) * 1.25f
    }
}

/** All close affordances share a request and delivery gate. */
internal class SheetClosePolicy {
    private var requested = false
    private var delivered = false

    fun request(allowDismiss: () -> Boolean = { true }): Boolean {
        if (requested || !allowDismiss()) return false
        requested = true
        return true
    }

    fun deliver(): Boolean {
        if (!requested || delivered) return false
        delivered = true
        return true
    }

    fun withdraw() {
        if (!delivered) requested = false
    }
}
