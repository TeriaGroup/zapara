package ru.bgtu_voenmeh.zapara.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class IntersectionTest {

    @Test
    fun overlap() {
        assertTrue(Intersection.timesOverlap("09:00", "10:35", "09:00", "10:35"))
        assertTrue(Intersection.timesOverlap("09:00", "10:35", "10:00", "11:35"))
        assertFalse(Intersection.timesOverlap("09:00", "10:35", "10:35", "12:10"))
        assertFalse(Intersection.timesOverlap(null, null, "09:00", "10:35"))
    }

    @Test
    fun scores() {
        // same room
        assertEquals(100, Intersection.scoreOf("326", "УЛК", "326", "УЛК"))
        // same building + same floor (326 vs 320, floor 3)
        assertEquals(75, Intersection.scoreOf("326", "УЛК", "320", "УЛК"))
        // same building, different floor
        assertEquals(50, Intersection.scoreOf("326", "УЛК", "450", "УЛК"))
        // same time only = in uni (NOT red)
        assertEquals(25, Intersection.scoreOf("326", "УЛК", "324", "ГК"))
    }

    @Test
    fun scoreTexts() {
        assertEquals("в той же аудитории", Intersection.scoreToTextRu(100))
        assertEquals("на том же этаже", Intersection.scoreToTextRu(75))
        assertEquals("в том же корпусе", Intersection.scoreToTextRu(50))
        assertEquals("в вузе", Intersection.scoreToTextRu(25))
        assertEquals("нет на месте", Intersection.scoreToTextRu(0))
        // Russian-only product: retain all thresholds and cover the upper boundary.
        assertEquals("в той же аудитории", Intersection.scoreToTextRu(101))
    }

    @Test
    fun floorOf() {
        assertEquals(3, Intersection.floorOf("326"))
        assertEquals(5, Intersection.floorOf("507а"))
        assertEquals(0, Intersection.floorOf(""))
        assertEquals(0, Intersection.floorOf(null))
    }
}
