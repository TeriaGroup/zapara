package ru.bgtu_voenmeh.zapara.ui.friends

import org.junit.Assert.assertEquals
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.XmlCopy

class StrictnessTest {
    @Test fun labels() {
        assertEquals("в вузе", Strictness.label(25, XmlCopy))
        assertEquals("аудитория", Strictness.label(100, XmlCopy))
        assertEquals("корпус", Strictness.label(50, XmlCopy))
        assertEquals("этаж", Strictness.label(75, XmlCopy))
    }

    @Test fun nearest_step() {
        assertEquals(50, Strictness.nearest(60))
        assertEquals(25, Strictness.nearest(30))
        assertEquals(100, Strictness.nearest(90))
    }
}
