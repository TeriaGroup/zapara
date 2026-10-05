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
    fun timeSuffixFillsMissingWeekCode() {
        val xml = """<Timetable>
  <Period Title="t" StartYear="2026" StartMonth="9" StartDay="1" />
  <Weeks WeekCount="2" />
  <Group Number="А863С" IdGroup="3313">
    <Days><Day Title="Понедельник"><GroupLessons>
      <Lesson><WeekCode></WeekCode><Time>9:00 Нечетная</Time><Discipline>лек А</Discipline><Lecturers /><Classroom>1;</Classroom></Lesson>
      <Lesson><WeekCode>0</WeekCode><Time>10:50 Четная</Time><Discipline>лек Б</Discipline><Lecturers /><Classroom>1;</Classroom></Lesson>
      <Lesson><WeekCode>1</WeekCode><Time>12:40 Четная</Time><Discipline>лек В</Discipline><Lecturers /><Classroom>1;</Classroom></Lesson>
    </GroupLessons></Day></Days>
  </Group>
</Timetable>"""
        val lessons = GroupParser.parse(xml).lessons
        assertEquals(listOf(1, 2, 1), lessons.map { it.parity })
    }

    @Test
    fun html_landing_page_is_refused_before_xml_parse() {
        val html = """<!doctype html>
<html lang="ru"><head><title>ВОЕНМЕХ</title></head><body><div id="app"></div></body></html>"""
        val err = try {
            GroupParser.parse(html)
            null
        } catch (t: Throwable) { t.message.orEmpty() }
        assertEquals(TimetablePayload.NOT_XML, err)
        assertEquals(TimetablePayload.NOT_XML, try {
            TimetablePayload.requireXml(html, "text/html; charset=utf-8")
            "ok"
        } catch (t: Throwable) { t.message })
        val xml = """<?xml version="1.0"?><Timetable>
  <Period Title="t" StartYear="2026" StartMonth="9" StartDay="1"/>
  <Weeks WeekCount="2"/>
</Timetable>"""
        assertEquals(xml, TimetablePayload.requireXml(xml, "application/xml"))
        val hummingbird = "<!-- This page is cached by the Hummingbird Performance plugin --><!DOCTYPE html><html></html>"
        assertEquals(TimetablePayload.NOT_XML, try {
            TimetablePayload.requireXml(hummingbird)
            "ok"
        } catch (t: Throwable) { t.message })
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
