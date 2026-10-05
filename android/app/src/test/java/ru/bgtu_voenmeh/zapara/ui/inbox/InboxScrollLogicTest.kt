package ru.bgtu_voenmeh.zapara.ui.inbox

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class InboxScrollLogicTest {
    @Test fun firstLoadedMessageCanScrollIntoView() {
        assertTrue(shouldAutoScroll(null, "first", nearBottom = false))
    }

    @Test fun incomingMessageDoesNotInterruptHistoryReading() {
        assertFalse(shouldAutoScroll("first", "second", nearBottom = false))
        assertTrue(shouldAutoScroll("first", "second", nearBottom = true))
        assertFalse(shouldAutoScroll("second", "second", nearBottom = true))
    }
}
