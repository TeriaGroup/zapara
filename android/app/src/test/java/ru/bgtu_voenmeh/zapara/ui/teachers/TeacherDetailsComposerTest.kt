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

    @Test fun labels_are_explicit_for_known_and_unknown_parity() {
        val expected = mapOf(0 to "Обе недели", 1 to "Нечётная неделя", 2 to "Чётная неделя",
            -1 to "Чётность не указана", 3 to "Чётность не указана", Int.MAX_VALUE to "Чётность не указана")
        expected.forEach { (parity, label) ->
            assertEquals(label, TeacherDetailsComposer.parityLabel(parity, XmlCopy))
        }
    }

    @Test fun actual_composition_preserves_same_pair_different_parity_and_both_records() {
        val original = parsed.lessons.first().copy(dayOfWeek = 1, timeStart = "09:00")
        val lessons = listOf(0, 1, 2, -1).map { original.copy(parity = it) }
        val expected = listOf(listOf(0, 1, 2, -1), listOf(0, 1), listOf(0, 2))
        expected.forEachIndexed { filter, parities ->
            val days = TeacherDetailsComposer.compose(lessons, filter, original.groups.first().idGroup, XmlCopy)
            assertEquals(listOf(1), days.map { it.dow })
            val rows = days.single().rows
            assertEquals(parities, rows.map { it.parity })
            assertEquals(1, rows.map { it.subject }.distinct().size)
            assertTrue(rows.all { it.time == "09:00" && it.isMyGroup })
            assertEquals(1, rows.count { it.parity == 0 })
            assertEquals(parities.map { TeacherDetailsComposer.parityLabel(it, XmlCopy) },
                rows.map { TeacherDetailsComposer.parityLabel(it.parity, XmlCopy) })
        }
    }

    @Test fun unknown_filter_keeps_existing_all_behavior_and_days_remain_sorted() {
        val original = parsed.lessons.first()
        val lessons = listOf(original.copy(dayOfWeek = 2, parity = 9), original.copy(dayOfWeek = 1, parity = 0))
        val days = TeacherDetailsComposer.compose(lessons, 9, "not-my-group", XmlCopy)
        assertEquals(listOf(1, 2), days.map { it.dow })
        assertTrue(days.flatMap { it.rows }.none { it.isMyGroup })
        assertEquals("Чётность не указана", TeacherDetailsComposer.parityLabel(days.last().rows.single().parity, XmlCopy))
        assertTrue(TeacherDetailsComposer.compose(emptyList(), 0, "", XmlCopy).isEmpty())
    }

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
