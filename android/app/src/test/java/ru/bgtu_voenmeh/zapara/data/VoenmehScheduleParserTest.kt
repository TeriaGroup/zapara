package ru.bgtu_voenmeh.zapara.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.LocalDate

class VoenmehScheduleParserTest {
    private val metaJson = """
        {
          "has_data": true,
          "updated_at": "2026-09-10T09:16:14.998513Z",
          "period": "ОСЕННИЙ СЕМЕСТР 2026/2027 уч. г.",
          "groups": ["А863С", "09С33"],
          "lecturers": ["Барт Е.Л."],
          "times": ["9:00", "10:50"],
          "lessons_count": 4
        }
    """.trimIndent()

    private val lessonsJson = """
        {
          "owner_kind": "group",
          "name": "А863С",
          "department": "",
          "lessons": [
            {
              "id": 30670,
              "owner_kind": "group",
              "owner_name": "А863С",
              "day": 1,
              "time": "9:00",
              "week": "odd",
              "kind": "лек",
              "subject": "ВЫСШ. МАТ.",
              "teachers": ["Барт Е.Л."],
              "groups": [],
              "rooms": ["493"]
            },
            {
              "id": 30674,
              "owner_kind": "group",
              "owner_name": "А863С",
              "day": 1,
              "time": "12:40",
              "week": "odd",
              "kind": "пр",
              "subject": "ОСН.РОС.ГОС",
              "teachers": ["Лысенко Е.М."],
              "groups": [],
              "rooms": ["563*"]
            },
            {
              "id": 30688,
              "owner_kind": "group",
              "owner_name": "А863С",
              "day": 3,
              "time": "12:40",
              "week": "odd",
              "kind": "лаб",
              "subject": "ФИЗИКА",
              "teachers": [],
              "groups": [],
              "rooms": ["323*"]
            },
            {
              "id": 30672,
              "owner_kind": "group",
              "owner_name": "А863С",
              "day": 1,
              "time": "10:50",
              "week": "even",
              "kind": "пр",
              "subject": "ЭК ПО ФК И СПОРТУ",
              "teachers": [],
              "groups": [],
              "rooms": []
            }
          ]
        }
    """.trimIndent()

    @Test fun meta_reads_autumn_period_and_group_names() {
        val meta = VoenmehScheduleParser.parseMeta(metaJson)
        assertEquals(LocalDate.of(2026, 9, 1), meta.periodStart)
        assertEquals(2, meta.weekCount)
        assertEquals("ОСЕННИЙ СЕМЕСТР 2026/2027 уч. г.", meta.periodTitle)
        assertEquals(listOf("А863С", "09С33"), meta.groups)
    }

    @Test fun lessons_map_type_parity_building_and_time_end() {
        val lessons = VoenmehScheduleParser.parseLessons(lessonsJson, "А863С")
        assertEquals(4, lessons.size)
        val math = lessons.first { it.subjectRaw.contains("ВЫСШ") }
        assertEquals(1, math.dayOfWeek)
        assertEquals(1, math.parity)
        assertEquals("09:00", math.timeStart)
        assertEquals("10:35", math.timeEnd)
        assertEquals("лек", math.typeRaw)
        assertEquals("лек ВЫСШ. МАТ.", math.subjectRaw)
        assertEquals("Барт Е.Л.", math.teacherRaw)
        assertEquals("ГК", math.buildingRaw)
        assertEquals("493", math.roomRaw)
        val org = lessons.first { it.subjectRaw.contains("ОСН") }
        assertEquals("пр", org.typeRaw)
        assertEquals("УЛК", org.buildingRaw)
        assertEquals("563", org.roomRaw)
        assertEquals("563*;", org.classroomRaw)
        val sport = lessons.first { it.subjectRaw.contains("ФК") }
        assertEquals(2, sport.parity)
        assertEquals("", sport.classroomRaw)
        assertEquals("", sport.teacherRaw)
        assertEquals("", sport.buildingRaw)
        assertEquals("—", ru.bgtu_voenmeh.zapara.ui.LessonFormat.roomLabel(sport, ru.bgtu_voenmeh.zapara.ui.XmlCopy))
    }

    @Test fun spring_period_uses_the_second_year() {
        val meta = VoenmehScheduleParser.parseMeta(
            """{"has_data":true,"period":"ВЕСЕННИЙ СЕМЕСТР 2025/2026 уч. г.","groups":["А863С"]}"""
        )
        assertEquals(LocalDate.of(2026, 2, 9), meta.periodStart)
    }

    @Test fun lessons_are_numbered_by_time_within_day_and_parity() {
        val shuffled = """
            {"lessons":[
              {"day":1,"time":"10:50","week":"odd","kind":"пр","subject":"ФК","teachers":[],"rooms":[]},
              {"day":1,"time":"12:40","week":"odd","kind":"пр","subject":"ОСН","teachers":[],"rooms":["563*"]},
              {"day":1,"time":"9:00","week":"odd","kind":"лек","subject":"МАТ.","teachers":[],"rooms":["493"]},
              {"day":3,"time":"9:00","week":"odd","kind":"лек","subject":"ИСТОРИЯ","teachers":[],"rooms":[]}
            ]}
        """.trimIndent()
        val lessons = VoenmehScheduleParser.parseLessons(shuffled, "А863С")
        val monday = lessons.filter { it.dayOfWeek == 1 && it.parity == 1 }.sortedBy { it.index }
        assertEquals(listOf("09:00", "10:50", "12:40"), monday.map { it.timeStart })
        assertEquals(listOf(1, 2, 3), monday.map { it.index })
        val wednesday = lessons.single { it.dayOfWeek == 3 }
        assertEquals(1, wednesday.index)
    }

    @Test fun assemble_keeps_empty_groups_and_uses_name_as_id() {
        val parsed = VoenmehScheduleParser.assemble(
            VoenmehScheduleParser.parseMeta(metaJson),
            listOf("А863С" to VoenmehScheduleParser.parseLessons(lessonsJson, "А863С"))
        )
        assertEquals(setOf("А863С", "09С33"), parsed.groups.map { it.id }.toSet())
        assertEquals("А863С", parsed.groups.first { it.name == "А863С" }.id)
        assertTrue(parsed.lessons.all { it.groupId == "А863С" })
        assertEquals(VoenmehScheduleClient.ORIGIN, parsed.groups.first().url)
    }

    @Test fun html_is_not_a_schedule_payload() {
        val err = try {
            VoenmehScheduleParser.parseMeta("<!doctype html><html></html>")
            "ok"
        } catch (t: Throwable) { t.message.orEmpty() }
        assertTrue(err.contains("расписани") || err == TimetablePayload.NOT_XML)
        val hummingbird = try {
            VoenmehScheduleParser.parseMeta("<!-- This page is cached --><!DOCTYPE html><html></html>")
            "ok"
        } catch (t: Throwable) { t.message.orEmpty() }
        assertEquals(TimetablePayload.NOT_XML, hummingbird)
    }
}
