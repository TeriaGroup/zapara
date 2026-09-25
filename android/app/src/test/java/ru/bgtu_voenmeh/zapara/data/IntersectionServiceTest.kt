package ru.bgtu_voenmeh.zapara.data

import org.junit.Assert.assertEquals
import org.junit.Test
import java.time.LocalDate

class IntersectionServiceTest {
    private val date = LocalDate.of(2026, 9, 14)
    private val own = Lesson(groupId = "3313", dayOfWeek = 1, parity = 1,
        timeStart = "09:00", timeEnd = "10:35", roomRaw = "493", buildingRaw = "ГК")

    @Test fun one_friend_uses_the_best_simultaneous_lesson_even_when_it_is_first() {
        val sameRoom = own.copy(groupId = "3031", classroomRaw = "493")
        val sameFloor = own.copy(groupId = "3031", roomRaw = "494", classroomRaw = "494")
        val result = IntersectionService.intersections(own, date,
            listOf(Friend("09С31", "#F2A33C", true, "Иван")), 75,
            LocalDate.of(2026, 9, 1), 2, false,
            lessonsFor = { _, _, _ -> listOf(sameRoom, sameFloor) },
            resolveId = { "3031" })
        assertEquals(1, result.size)
        assertEquals(100, result.single().score)
        assertEquals("493", result.single().room)
    }
}
