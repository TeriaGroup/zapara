package ru.bgtu_voenmeh.zapara.ui.summary

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.GROUP_FIXTURE
import ru.bgtu_voenmeh.zapara.data.GroupParser
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.ui.XmlCopy

class SummaryComposerTest {
    private val fixture = listOf(
        Lesson(dayOfWeek = 1, parity = 1, typeRaw = "лек", subjectRaw = "Математика", teacherRaw = "Иванов", classroomRaw = "101;"),
        Lesson(dayOfWeek = 1, parity = 2, typeRaw = "лек", subjectRaw = "Математика", teacherRaw = "Иванов", classroomRaw = "102;"),
        Lesson(dayOfWeek = 1, parity = 0, typeRaw = "лек", subjectRaw = "Математика", teacherRaw = "Иванов", classroomRaw = "101;"),
        Lesson(dayOfWeek = 2, parity = 1, typeRaw = "лек", subjectRaw = "Математика", teacherRaw = "Иванов", classroomRaw = "101;")
    )

    @Test fun all_five_breakdowns_share_filtered_records_without_doubling_both() {
        listOf(3, 2, 4).forEachIndexed { segment, total ->
            val tiles = SummaryComposer.tiles(segment, fixture, { _, day -> "Предмет $day" }, XmlCopy)
            assertEquals(total, tiles.total)
            assertEquals((1..6).toList(), tiles.byDay.map { it.first })
            assertEquals(if (segment == 2) 3 else 2, tiles.byDay.first().second)
            assertEquals(if (segment == 1) 0 else 1, tiles.byDay[1].second)
            assertEquals(listOf(0, 0, 0, 0), tiles.byDay.drop(2).map { it.second })
            listOf(tiles.byDay.sumOf { it.second }, tiles.byRoom.sumOf { it.second },
                tiles.byType.sumOf { it.second }, tiles.bySubject.sumOf { it.second },
                tiles.byTeacher.sumOf { it.second }).forEach { assertEquals(total, it) }
            assertEquals(tiles.byRoom.map { it.first }, tiles.rooms)
            assertEquals(if (segment == 1) listOf("101 ГК" to 1, "102 ГК" to 1)
                else if (segment == 0) listOf("101 ГК" to 3)
                else listOf("101 ГК" to 3, "102 ГК" to 1), tiles.byRoom)
            assertEquals("Предмет 1", tiles.bySubject.first().first)
        }
    }

    @Test fun empty_has_six_zero_days_and_no_rooms() {
        val tiles = SummaryComposer.tiles(2, emptyList(), { _, _ -> "" }, XmlCopy)
        assertEquals((1..6).map { it to 0 }, tiles.byDay)
        assertEquals(0, tiles.total)
        assertTrue(tiles.byRoom.isEmpty())
        assertTrue(tiles.rooms.isEmpty())
    }

    @Test fun sunday_is_included_only_in_the_filtered_input() {
        val lessons = fixture + fixture[0].copy(dayOfWeek = 7, parity = 2)
        assertEquals((1..6).toList(), SummaryComposer.tiles(0, lessons, { _, _ -> "" }, XmlCopy).byDay.map { it.first })
        val tiles = SummaryComposer.tiles(1, lessons, { _, _ -> "" }, XmlCopy)
        assertEquals(7 to 1, tiles.byDay.last())
        assertEquals(tiles.total, tiles.byDay.sumOf { it.second })
    }

    @Test fun missing_rooms_do_not_create_building_only_groups_or_change_other_counts() {
        val lessons = listOf("", "   ", "—", "—;").map { fixture[0].copy(classroomRaw = it) }
        val tiles = SummaryComposer.tiles(2, lessons, { _, _ -> "" }, XmlCopy)
        assertEquals(4, tiles.total)
        assertEquals(4, tiles.bySubject.sumOf { it.second })
        assertTrue(tiles.byRoom.isEmpty())
    }

    @Test fun long_and_remote_rooms_keep_existing_formatter_and_sort_order() {
        val longRoom = "Учебная лаборатория вычислительной техники"
        val lessons = listOf(fixture[0].copy(roomRaw = longRoom), fixture[0].copy(classroomRaw = "дистанционно"))
        val tiles = SummaryComposer.tiles(2, lessons, { _, _ -> "" }, XmlCopy)
        val expected = lessons.map { ru.bgtu_voenmeh.zapara.ui.LessonFormat.roomLabel(it, XmlCopy) to 1 }.sortedBy { it.first }
        assertEquals(expected, tiles.byRoom)
        assertEquals(2, tiles.byRoom.sumOf { it.second })
    }

    private val parsed by lazy { GroupParser.parse(GROUP_FIXTURE) }
    private val mine get() = parsed.lessons.filter { it.groupId == "3313" }
    private val mathNorm = Parity.normalizeSubject("лек ВЫСШ. МАТЕМАТ")

    @Test fun both_weeks_counts_types_subjects_rooms() {
        val tiles = SummaryComposer.tiles(2, mine, { norm, _ -> if (norm == mathNorm) "Матан" else "" }, XmlCopy)
        assertEquals(mine.size, tiles.total)
        assertTrue(tiles.byType.any { it.first == "лекция" && it.second > 0 })
        assertEquals(tiles.bySubject, tiles.bySubject.sortedByDescending { it.second })
        assertEquals(tiles.rooms.size, tiles.rooms.toSet().size)
        assertFalse(tiles.rooms.any { it.isBlank() })
        assertTrue(tiles.rooms.any { it.contains("493") })
    }

    @Test fun odd_segment_keeps_both_and_odd_parity() {
        val oddish = mine.filter { it.parity == 0 || it.parity == 1 }
        val tiles = SummaryComposer.tiles(0, mine, { _, _ -> "" }, XmlCopy)
        assertEquals(oddish.size, tiles.total)
        val mixed = listOf(
            Lesson(parity = 2, typeRaw = "лек", subjectRaw = "A", classroomRaw = "1;"),
            Lesson(parity = 1, typeRaw = "пр", subjectRaw = "B", classroomRaw = "2;"),
            Lesson(parity = 0, typeRaw = "лек", subjectRaw = "C", classroomRaw = "3;")
        )
        val evenOnly = SummaryComposer.tiles(1, mixed, { _, _ -> "" }, XmlCopy)
        assertEquals(2, evenOnly.total)
    }
}
