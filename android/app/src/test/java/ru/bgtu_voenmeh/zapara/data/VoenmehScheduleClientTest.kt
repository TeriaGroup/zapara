package ru.bgtu_voenmeh.zapara.data

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class VoenmehScheduleClientTest {
    @Test fun pulls_meta_and_lessons_without_xml() = runBlocking {
        val http = mutableListOf<String>()
        val client = VoenmehScheduleClient { url ->
            http += url
            when {
                url.endsWith("/meta") -> """{"has_data":true,"period":"ОСЕННИЙ СЕМЕСТР 2026/2027 уч. г.","groups":["А863С"],"updated_at":"2026-09-10T09:16:14Z"}"""
                url.contains("lessons") -> """{"name":"А863С","lessons":[{"day":1,"time":"9:00","week":"odd","kind":"лек","subject":"ВЫСШ. МАТ.","teachers":["Барт Е.Л."],"rooms":["493"]}]}"""
                else -> error(url)
            }
        }
        val parsed = client.fetchSchedule()
        assertEquals(1, parsed.groups.size)
        assertEquals("А863С", parsed.groups.single().name)
        assertEquals("09:00", parsed.lessons.single().timeStart)
        assertTrue(http.any { it.contains("/api/schedule/meta") })
        assertTrue(http.none { it.contains("TimetableGroup50.xml") })
    }

    @Test fun fetches_only_named_groups_and_keeps_catalog() = runBlocking {
        val http = mutableListOf<String>()
        val client = VoenmehScheduleClient { url ->
            http += url
            when {
                url.endsWith("/meta") ->
                    """{"has_data":true,"period":"ОСЕННИЙ СЕМЕСТР 2026/2027 уч. г.","groups":["А863С","09С33"],"updated_at":"2026-09-10T09:16:14Z"}"""
                url.contains("lessons") && url.contains(VoenmehScheduleClient.encode("А863С")) ->
                    """{"name":"А863С","lessons":[{"day":1,"time":"9:00","week":"odd","kind":"лек","subject":"ВЫСШ. МАТ.","teachers":["Барт Е.Л."],"rooms":["493"]}]}"""
                else -> error(url)
            }
        }
        val parsed = client.fetchSchedule(listOf("А863С"))
        assertEquals(setOf("А863С", "09С33"), parsed.groups.map { it.name }.toSet())
        assertEquals(1, parsed.lessons.size)
        assertEquals("А863С", parsed.lessons.single().groupId)
        assertEquals(1, http.count { it.contains("/api/schedule/lessons") })
        assertTrue(http.none { it.contains(VoenmehScheduleClient.encode("09С33")) })
    }

    @Test fun skips_a_group_when_lessons_are_html() = runBlocking {
        val client = VoenmehScheduleClient { url ->
            when {
                url.endsWith("/meta") ->
                    """{"has_data":true,"period":"ОСЕННИЙ СЕМЕСТР 2026/2027 уч. г.","groups":["А863С","09С33"]}"""
                url.contains(VoenmehScheduleClient.encode("А863С")) ->
                    """{"name":"А863С","lessons":[{"day":1,"time":"9:00","week":"odd","kind":"лек","subject":"МАТ.","teachers":[],"rooms":["493"]}]}"""
                else -> "<!doctype html><html></html>"
            }
        }
        val parsed = client.fetchSchedule()
        assertEquals(1, parsed.lessons.size)
        assertEquals("А863С", parsed.lessons.single().groupId)
    }
}
