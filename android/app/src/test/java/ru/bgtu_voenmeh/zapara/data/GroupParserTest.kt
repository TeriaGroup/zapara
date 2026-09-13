package ru.bgtu_voenmeh.zapara.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.LocalDate

class GroupParserTest {

    private val parsed by lazy { GroupParser.parse(GROUP_FIXTURE) }

    @Test
    fun headerAndGroups() {
        assertEquals(LocalDate.of(2026, 9, 1), parsed.periodStart)
        assertEquals(2, parsed.weekCount)
        assertEquals(2, parsed.groups.size)
        assertEquals("3313", parsed.groups.first { it.name == "А863С" }.id)
    }

    @Test
    fun lessons3313Split() {
        val mine = parsed.lessons.filter { it.groupId == "3313" }
        assertEquals(7, mine.size)
        assertEquals(5, mine.count { it.parity == 1 })
        assertEquals(2, mine.count { it.parity == 2 })
    }

    @Test
    fun timeEndPlus95() {
        val first = parsed.lessons.first { it.groupId == "3313" && it.timeStart == "09:00" }
        assertEquals("10:35", first.timeEnd)
    }

    @Test
    fun typeAndBuildingMapping() {
        val math = parsed.lessons.first { it.subjectRaw == "лек ВЫСШ. МАТЕМАТ" && it.parity == 1 }
        assertEquals("лек", math.typeRaw)
        assertEquals("ГК", math.buildingRaw) // no star -> ГК
        assertEquals("493", math.roomRaw)
        val org = parsed.lessons.first { it.subjectRaw == "пр ОСН РОС ГОС" }
        assertEquals("пр", org.typeRaw)
        assertEquals("УЛК", org.buildingRaw) // star = УЛК
        assertEquals("563", org.roomRaw)
        val vc = parsed.lessons.first { it.classroomRaw == "ВЦ 280;" }
        assertEquals("ВЦ", vc.buildingRaw)
        assertEquals("280", vc.roomRaw)
        val remote = parsed.lessons.first { it.subjectRaw == "лек ФК И СПОРТ" }
        assertEquals("дистанционно", remote.roomRaw)
        val empty = parsed.lessons.first { it.subjectRaw == "пр ЭК ПО ФК И СПОРТУ" }
        assertEquals("", empty.teacherRaw)
        assertEquals("", empty.classroomRaw)
    }

    @Test
    fun bundledAssetParses() {
        val file = java.io.File("src/main/assets/TimetableGroup50.xml")
        org.junit.Assume.assumeTrue(file.isFile)
        file.inputStream().use { stream ->
            val parsed = GroupParser.parse(stream)
            assertTrue(parsed.groups.size > 100)
            assertTrue(parsed.lessons.size > 1000)
            assertEquals(LocalDate.of(2026, 9, 1), parsed.periodStart)
        }
    }
}
