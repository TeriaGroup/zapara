package ru.bgtu_voenmeh.zapara.ui.teachers

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.LECTURER_FIXTURE
import ru.bgtu_voenmeh.zapara.data.LecturerParser
import ru.bgtu_voenmeh.zapara.ui.XmlCopy

class TeacherDetailsComposerTest {
    private val parsed by lazy { LecturerParser.parse(LECTURER_FIXTURE) }

    @Test fun compose_keeps_days_with_lessons_and_marks_own_group() {
        val days = TeacherDetailsComposer.compose(parsed.lessons, 0, "3313", XmlCopy)
        assertTrue(days.isNotEmpty())
        assertTrue(days.all { it.rows.isNotEmpty() })
        val mine = days.flatMap { it.rows }.filter { it.isMyGroup }
        assertTrue(mine.isNotEmpty())
        assertTrue(mine.any { it.groups.contains("А863С") })
    }

    @Test fun odd_filter_keeps_both_and_odd() {
        val odd = TeacherDetailsComposer.compose(parsed.lessons, 1, "3313", XmlCopy)
        assertTrue(odd.flatMap { it.rows }.all { it.parity == 0 || it.parity == 1 })
        val even = TeacherDetailsComposer.compose(parsed.lessons, 2, "3313", XmlCopy)
        assertTrue(even.flatMap { it.rows }.all { it.parity == 0 || it.parity == 2 })
        val bothCount = TeacherDetailsComposer.compose(parsed.lessons, 0, "3313", XmlCopy).sumOf { it.rows.size }
        assertEquals(parsed.lessons.size, bothCount)
        assertFalse(odd.sumOf { it.rows.size } == 0 && parsed.lessons.isNotEmpty())
    }
}
