package ru.bgtu_voenmeh.zapara

import androidx.test.ext.junit.runners.AndroidJUnit4
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import ru.bgtu_voenmeh.zapara.data.VoenmehScheduleClient
import ru.bgtu_voenmeh.zapara.data.VoenmehScheduleParser

@RunWith(AndroidJUnit4::class)
class VoenmehLiveScheduleTest {
    @Test fun emulator_parses_live_university_json_for_a863s() {
        val meta = VoenmehScheduleParser.parseMeta(
            VoenmehScheduleClient.httpGet(VoenmehScheduleClient.META_URL)
        )
        assertTrue("catalog must include А863С", meta.groups.contains("А863С"))
        val url = VoenmehScheduleClient.LESSONS_URL +
            "?name=" + VoenmehScheduleClient.encode("А863С") + "&type=group"
        val lessons = VoenmehScheduleParser.parseLessons(VoenmehScheduleClient.httpGet(url), "А863С")
        assertTrue("live group must have lessons, got ${lessons.size}", lessons.size >= 8)
        assertTrue(lessons.any { it.timeStart.isNotEmpty() })
        assertTrue(lessons.any { it.buildingRaw == "ГК" || it.buildingRaw == "УЛК" })
        assertTrue(lessons.any { it.parity == 1 })
        assertTrue(lessons.any { it.parity == 2 })
    }

    @Test fun emulator_fetch_schedule_loads_catalog_and_selected_group() = runBlocking {
        val parsed = VoenmehScheduleClient().fetchSchedule(listOf("А863С"))
        assertTrue("catalog must be the live group list, got ${parsed.groups.size}", parsed.groups.size >= 100)
        assertTrue(parsed.groups.any { it.name == "А863С" })
        val lessons = parsed.lessons.filter { it.groupId == "А863С" }
        assertTrue("selected group must have lessons, got ${lessons.size}", lessons.size >= 8)
        assertTrue(lessons.any { it.buildingRaw == "ГК" || it.buildingRaw == "УЛК" })
        assertTrue(parsed.periodTitle.contains("2026"))
    }
}
