package ru.bgtu_voenmeh.zapara.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.LocalDate

class ScheduleTest {

    private val ps = LocalDate.of(2026, 9, 1)
    private val all by lazy { GroupParser.parse(GROUP_FIXTURE).lessons }

    @Test
    fun wednesdayOdd() {
        // 2026-09-02 = Wednesday, week 1 odd -> 1 lesson (лек ИСТОРИЯ)
        val lessons = Schedule.lessonsForDate(all, "3313", LocalDate.of(2026, 9, 2), ps)
        assertEquals(1, lessons.size)
        assertEquals("лек ИСТОРИЯ", lessons[0].subjectRaw)
    }

    @Test
    fun sundayEmpty() {
        assertTrue(Schedule.lessonsForDate(all, "3313", LocalDate.of(2026, 9, 6), ps).isEmpty())
    }

    @Test
    fun nextBySubject() {
        // From Wed 09-02, next Monday with this subject is 07.09 (week 2 even).
        val norm = Parity.normalizeSubject("лек ВЫСШ. МАТЕМАТ")
        val next = Schedule.nextOccurrenceBySubject(all, "3313", norm, LocalDate.of(2026, 9, 2), ps)
        assertEquals(LocalDate.of(2026, 9, 7), next)
    }

    @Test
    fun nextByTeacher() {
        // Волченкова Н.М. next from Wed 09-02 -> Wed 09-02 is even lesson? 09-02 is odd week; her lesson is even -> next Wed 09-09
        val next = Schedule.nextOccurrenceByTeacher(all, "3313", "Волченкова Н.М.", LocalDate.of(2026, 9, 2), ps)
        assertEquals(LocalDate.of(2026, 9, 9), next)
    }

    @Test
    fun nextUnknownIsNull() {
        assertNull(Schedule.nextOccurrenceBySubject(all, "3313", "несуществующий предмет", LocalDate.of(2026, 9, 2), ps))
        assertNull(Schedule.nextOccurrenceByTeacher(all, "3313", "—", LocalDate.of(2026, 9, 2), ps))
    }
}
