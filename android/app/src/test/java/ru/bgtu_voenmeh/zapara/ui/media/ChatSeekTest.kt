package ru.bgtu_voenmeh.zapara.ui.media

import org.junit.Assert.assertEquals
import org.junit.Test

class ChatSeekTest {
    @Test fun seekStepsClampToActualDurationAndDoNotOverflow() {
        assertEquals(0, chatSeekPosition(5000, -10000, 60000))
        assertEquals(60000, chatSeekPosition(55000, 10000, 60000))
        assertEquals(30000, chatSeekPosition(20000, 10000, 60000))
        assertEquals(Int.MAX_VALUE, chatSeekPosition(Int.MAX_VALUE - 1, 10000, Int.MAX_VALUE))
        assertEquals(0, chatSeekPosition(1000, 10000, -1))
    }
}
