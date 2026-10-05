package ru.bgtu_voenmeh.zapara.ui.gestures

import org.junit.Assert.*
import org.junit.Test

class PlannerSwipePolicyTest {
    private fun gesture(startX: Float = 180f) = PlannerSwipePolicy(startX, 100f, 360f, 12f, 64f, 24f)

    @Test fun deliberateLeftAndRightChooseAdjacentDays() {
        val left = gesture()
        assertTrue(left.move(100f, 108f))
        assertEquals(1L, left.finish()?.dayDelta)
        val right = gesture()
        assertTrue(right.move(260f, 108f))
        assertEquals(-1L, right.finish()?.dayDelta)
    }

    @Test fun aGestureEmitsOnlyOnce() {
        val swipe = gesture()
        swipe.move(100f, 100f)
        assertEquals(PlannerSwipeDirection.Next, swipe.finish())
        assertNull(swipe.finish())
    }

    @Test fun tapsAndShortHorizontalDragsDoNotNavigate() {
        val tap = gesture()
        assertFalse(tap.move(175f, 102f))
        assertNull(tap.finish())
        val short = gesture()
        assertTrue(short.move(130f, 100f))
        assertNull(short.finish())
    }

    @Test fun verticalScrollCannotBecomeNavigationAfterTurningHorizontal() {
        val swipe = gesture()
        assertFalse(swipe.move(176f, 130f))
        assertFalse(swipe.move(80f, 131f))
        assertNull(swipe.finish())
    }

    @Test fun diagonalIntentDoesNotCaptureScroll() {
        val swipe = gesture()
        assertFalse(swipe.move(160f, 119f))
        assertNull(swipe.finish())
    }

    @Test fun aClaimedDragThatTurnsVerticalDoesNotNavigate() {
        val swipe = gesture()
        assertTrue(swipe.move(100f, 103f))
        assertFalse(swipe.move(90f, 220f))
        assertNull(swipe.finish())
    }

    @Test fun secondPointerPermanentlyCancelsTheGesture() {
        val swipe = gesture()
        swipe.move(145f, 100f)
        assertFalse(swipe.move(100f, 100f, pointerCount = 2))
        swipe.move(80f, 100f)
        assertNull(swipe.finish())
    }

    @Test fun aChildConsumedMovementCancelsNavigation() {
        val swipe = gesture()
        assertFalse(swipe.move(100f, 100f, consumed = true))
        assertNull(swipe.finish())
    }

    @Test fun cancellationCannotEmitASelection() {
        val swipe = gesture()
        swipe.move(100f, 100f)
        swipe.cancel()
        assertNull(swipe.finish())
    }

    @Test fun bothSystemBackEdgesAreReserved() {
        for (start in listOf(0f, 23f, 337f, 360f)) {
            val swipe = gesture(start)
            assertFalse(swipe.move(180f, 100f))
            assertNull(swipe.finish())
        }
    }

    @Test fun releaseUsesFinalSignedDistanceRatherThanFurthestExcursion() {
        val swipe = gesture()
        swipe.move(95f, 100f)
        swipe.move(175f, 100f)
        assertNull(swipe.finish())
    }

    @Test fun weekSwipesAreBoundedToTheTwoExistingParityTabs() {
        assertEquals(1, PlannerSwipeDirection.Next.weekIndex(1))
        assertNull(PlannerSwipeDirection.Next.weekIndex(2))
        assertEquals(0, PlannerSwipeDirection.Previous.weekIndex(2))
        assertNull(PlannerSwipeDirection.Previous.weekIndex(1))
        assertNull(PlannerSwipeDirection.Next.weekIndex(0))
    }
}
