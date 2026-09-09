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
