package ru.bgtu_voenmeh.zapara.ui.friends

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Friend
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.ui.XmlCopy
import java.io.File
import java.time.LocalDate

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
}
