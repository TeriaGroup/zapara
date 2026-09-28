package ru.bgtu_voenmeh.zapara.ui.gestures

import org.junit.Assert.*
import org.junit.Test

class SheetDragPolicyTest {
    @Test fun deliberate_downward_release_closes_once() {
        val drag = SheetDragPolicy(touchSlop = 8f, dismissDistance = 64f)
        assertTrue(drag.move(5f, 64f))
        assertTrue(drag.finish())
        assertFalse(drag.finish())
    }

    @Test fun tap_and_short_drag_return_without_closing() {
        val tap = SheetDragPolicy(8f, 64f)
        assertFalse(tap.move(0f, 7f))
        assertFalse(tap.finish())
        val short = SheetDragPolicy(8f, 64f)
        assertTrue(short.move(0f, 63f))
        assertFalse(short.finish())
    }

    @Test fun upward_or_horizontal_start_cannot_turn_into_a_close() {
        for ((x, y) in listOf(0f to -9f, 12f to 5f)) {
            val drag = SheetDragPolicy(8f, 64f)
            assertFalse(drag.move(x, y))
            assertFalse(drag.move(0f, 100f))
            assertFalse(drag.finish())
        }
    }

    @Test fun returning_above_threshold_and_diagonal_release_do_not_close() {
        val returning = SheetDragPolicy(8f, 64f)
        returning.move(0f, 100f)
        returning.move(0f, 40f)
        assertFalse(returning.finish())
        val diagonal = SheetDragPolicy(8f, 64f)
        diagonal.move(0f, 30f)
        diagonal.move(80f, 80f)
        assertFalse(diagonal.finish())
    }

    @Test fun cancellation_and_multiple_pointers_invalidate_the_whole_drag() {
        val cancelled = SheetDragPolicy(8f, 64f)
        cancelled.move(0f, 100f)
        cancelled.cancel()
        assertFalse(cancelled.finish())
        val multiple = SheetDragPolicy(8f, 64f)
        multiple.move(0f, 100f)
        multiple.move(0f, 100f, pointerCount = 2)
        assertFalse(multiple.move(0f, 120f))
        assertFalse(multiple.finish())
    }

    @Test fun competing_close_requests_and_delivery_are_one_shot() {
        val close = SheetClosePolicy()
        assertFalse(close.deliver())
        assertTrue(close.request())
        assertFalse(close.request())
        assertTrue(close.deliver())
        assertFalse(close.deliver())
        assertFalse(close.request())
    }

    @Test fun refused_dismissal_keeps_the_panel_open_and_can_be_requested_again() {
        val close = SheetClosePolicy()
        assertFalse(close.request { false })
        assertFalse(close.deliver())
        assertTrue(close.request { true })
        assertTrue(close.deliver())
        assertFalse(close.request { error("already closing must not prompt again") })
    }

    @Test fun an_edit_during_exit_can_withdraw_the_pending_close_before_delivery() {
        val close = SheetClosePolicy()
        assertTrue(close.request())
        close.withdraw()
        assertFalse(close.deliver())
        assertTrue(close.request())
        assertTrue(close.deliver())
        close.withdraw()
        assertFalse(close.request())
    }
}
