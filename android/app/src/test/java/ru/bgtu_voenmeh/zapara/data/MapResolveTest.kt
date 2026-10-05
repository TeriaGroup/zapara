package ru.bgtu_voenmeh.zapara.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class MapResolveTest {

    @Test
    fun starIsUlk() {
        val mi = MapResolve.resolve("331*;")!!
        assertEquals("УЛК", mi.building)
        assertEquals(3, mi.floor)
        assertEquals("karta-ulk.-3-etazh-2022.jpg", mi.fileName)
        assertTrue(mi.hasMap)
    }

    @Test
    fun noStarIsGk() {
        val mi = MapResolve.resolve("324;")!!
        assertEquals("ГК", mi.building)
        assertEquals(3, mi.floor)
        assertEquals("karta-glavnyj-korpus-3-etazh-2022.jpg", mi.fileName)
    }

    @Test
    fun vcMapsToGk() {
        val mi = MapResolve.resolve("ВЦ 372*;")!!
        assertEquals("ВЦ", mi.building)
        assertEquals(3, mi.floor)
        assertEquals("karta-glavnyj-korpus-3-etazh-2022.jpg", mi.fileName)
    }

    @Test
    fun remoteAndEmpty() {
        val remote = MapResolve.resolve("дистанционно")!!
        assertTrue(remote.isRemote)
        assertFalse(remote.hasMap)
        assertNull(MapResolve.resolve(""))
        assertNull(MapResolve.resolve(null))
    }

    @Test
    fun floorClamp() {
        assertEquals(5, MapResolve.resolve("507*а;")!!.floor) // УЛК keeps 5
        assertEquals(1, MapResolve.resolve("101;")!!.floor) // ГК 1
    }

    @Test
    fun coordsLookup() {
        val coords = mapOf(
            "УЛК 3" to mapOf("320" to CoordsRect(0.52, 0.42, 0.07, 0.05))
        )
        val exact = MapResolve.findCoords(coords, "УЛК", 3, "320")
        assertEquals(0.52, exact!!.x, 0.0)
        val byDigits = MapResolve.findCoords(coords, "УЛК", 3, "320*")
        assertEquals(0.52, byDigits!!.x, 0.0)
        assertNull(MapResolve.findCoords(coords, "УЛК", 3, "999"))
        assertNull(MapResolve.findCoords(coords, "ГК", 3, "320"))
    }

    @Test
    fun lettered_room_keeps_the_suffix() {
        val mi = MapResolve.resolve("219А;")!!
        assertEquals("ГК", mi.building)
        assertEquals(2, mi.floor)
        assertEquals("219А", mi.roomRaw)
        val ulk = MapResolve.resolve("507*а;")!!
        assertEquals("УЛК", ulk.building)
        assertEquals(5, ulk.floor)
        assertEquals("507а", ulk.roomRaw)
    }

    @Test
    fun coords_prefer_lettered_key_over_digits_only_neighbour() {
        val coords = mapOf(
            "ГК 2" to mapOf(
                "219" to CoordsRect(0.10, 0.10, 0.05, 0.05),
                "219а" to CoordsRect(0.80, 0.80, 0.05, 0.05)
            )
        )
        val hit = MapResolve.findCoords(coords, "ГК", 2, "219А")
        assertEquals(0.80, hit!!.x, 0.0)
        assertEquals(0.80, hit.y, 0.0)
    }

    @Test
    fun vc_keeps_letter_suffix_and_does_not_treat_ke1_as_floor_one() {
        val lettered = MapResolve.resolve("ВЦ 219А;")!!
        assertEquals("ВЦ", lettered.building)
        assertEquals(2, lettered.floor)
        assertEquals("219А", lettered.roomRaw)
        val ke = MapResolve.resolve("ВЦ КЕ1;")!!
        assertEquals("ВЦ", ke.building)
        assertEquals("КЕ1", ke.roomRaw)
        assertEquals(1, ke.floor)
    }
}
