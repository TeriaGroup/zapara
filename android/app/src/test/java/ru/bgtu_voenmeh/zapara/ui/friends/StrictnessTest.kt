package ru.bgtu_voenmeh.zapara.ui.friends

import org.junit.Assert.assertEquals
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.XmlCopy

class StrictnessTest {
    @Test fun labels() {
        assertEquals("корпус", Strictness.label(25, XmlCopy))
        assertEquals("аудитория", Strictness.label(100, XmlCopy))
        assertEquals("корпус", Strictness.label(50, XmlCopy))
        assertEquals("этаж", Strictness.label(75, XmlCopy))
    }

    @Test fun nearest_step() {
        assertEquals(50, Strictness.nearest(60))
        assertEquals(50, Strictness.nearest(30))
        assertEquals(100, Strictness.nearest(90))
    }

    @Test fun legacy_and_out_of_range_values_use_remaining_levels() {
        listOf(Int.MIN_VALUE, -1, 0, 25, 49, 50).forEach { value ->
            assertEquals("value=$value", 50, Strictness.nearest(value))
        }
        assertEquals(100, Strictness.nearest(101))
        assertEquals(100, Strictness.nearest(Int.MAX_VALUE))
    }

    @Test fun nearest_level_boundaries() {
        val cases = listOf(62 to 50, 63 to 75, 75 to 75, 87 to 75, 88 to 100, 100 to 100)
        cases.forEach { (value, expected) ->
            assertEquals("value=$value", expected, Strictness.nearest(value))
        }
    }
}
