package ru.bgtu_voenmeh.zapara.ui.friends

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Friend
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.ui.XmlCopy
import java.io.File
import java.time.LocalDate
import java.time.LocalDateTime

class FriendsPreviewTest {
    private val period = LocalDate.of(2026, 9, 1)
    private val monday = LocalDate.of(2026, 9, 7)

    private val mine = Lesson(
        groupId = "3313", dayOfWeek = 1, parity = 1, timeStart = "09:00", timeEnd = "10:35",
        subjectRaw = "лек ВЫСШ. МАТЕМАТ", subjectNormalized = "высш. математика",
        typeRaw = "лек", roomRaw = "493", buildingRaw = "ГК"
    )

    @Test fun nearest_overlap_in_the_next_two_weeks() {
        val friendLesson = mine.copy(groupId = "3031", roomRaw = "494")
        val line = FriendsPreview.line(
            today = monday,
            myGroupId = "3313",
            friends = listOf(Friend("09С31", "#0", true, "Иван")),
            strictness = 25,
            periodStart = period,
            weekCount = 2,
            invert = false,
            allForGroup = { id -> if (id == "3313") listOf(mine) else listOf(friendLesson) },
            resolveId = { if (it == "09С31") "3031" else null },
            copy = XmlCopy
        )
        assertEquals("Пн 14.09 · 09:00 · ВЫСШ. МАТЕМАТ", line)
    }

    @Test fun empty_when_no_friend_is_around() {
        val line = FriendsPreview.line(
            today = monday,
            myGroupId = "3313",
            friends = listOf(Friend("09С31", "#0", true, "")),
            strictness = 25,
            periodStart = period,
            weekCount = 2,
            invert = false,
            allForGroup = { id -> if (id == "3313") listOf(mine) else emptyList() },
            resolveId = { if (it == "09С31") "3031" else null },
            copy = XmlCopy
        )
        assertEquals("В ближайшие две недели пересечений нет", line)
    }

    @Test fun section_shows_the_preview_line() {
        val src = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/friends/FriendsSection.kt").readText()
        assertTrue(src.contains("previewLine") || src.contains("Friends.Preview"))
    }

    @Test fun forecast_keeps_ongoing_pair_chooses_best_place_and_skips_finished_pair() {
        val friendLessons = listOf(
            mine.copy(groupId = "3031", roomRaw = "494"),
            mine.copy(groupId = "3031", roomRaw = "493")
        )
        val input = { now: LocalDateTime -> FriendsPreview.forecast(
            now = now, myGroupId = "3313",
            friends = listOf(Friend("09С31", "#0", true, "Иван")),
            strictness = 75, periodStart = period, weekCount = 2, invert = false,
            allForGroup = { id -> if (id == "3313") listOf(mine) else friendLessons },
            resolveId = { if (it == "09С31") "3031" else null }
        ) }
        val ongoing = input(LocalDateTime.of(2026, 9, 14, 9, 30))
        assertEquals(1, ongoing.encounters.size)
        assertEquals(100, ongoing.encounters.single().score)
        assertEquals("Иван", ongoing.encounters.single().members)
        assertEquals("493", ongoing.encounters.single().friendRoom)
        assertTrue(input(LocalDateTime.of(2026, 9, 14, 11, 0)).encounters.isEmpty())
    }

    @Test fun forecast_distinguishes_unloaded_group_from_loaded_empty_group() {
        val preview = FriendsPreview.forecast(
            now = LocalDateTime.of(2026, 9, 14, 8, 0), myGroupId = "3313",
            friends = listOf(Friend("A", "#0", true), Friend("B", "#1", true)),
            strictness = 25, periodStart = period, weekCount = 2, invert = false,
            allForGroup = { id -> if (id == "3313") listOf(mine) else emptyList() },
            resolveId = { it }, hasSchedule = { it == "B" }
        )
        assertEquals(listOf("A"), preview.missingGroups)
        assertEquals(1, preview.checkedGroups)
        assertTrue(preview.encounters.isEmpty())
    }

    @Test fun own_group_cannot_appear_as_a_friend_encounter() {
        val preview = FriendsPreview.forecast(
            now = LocalDateTime.of(2026, 9, 14, 8, 0), myGroupId = "3313",
            friends = listOf(Friend("А863С", "#0", true)),
            strictness = 25, periodStart = period, weekCount = 2, invert = false,
            allForGroup = { listOf(mine) }, resolveId = { "3313" }
        )
        assertTrue(preview.encounters.isEmpty())
    }
}
