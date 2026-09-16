package ru.bgtu_voenmeh.zapara.data

import org.junit.Assert.assertEquals
import org.junit.Test

class TeacherMatchTest {
    private val catalog = listOf(
        LecturerInfo("1", "Петров А.Б.", "кафедра"),
        LecturerInfo("2", "Петров С.К.", "кафедра"),
        LecturerInfo("3", "Петрова И.Л.", "кафедра"),
        LecturerInfo("4", "Барт Елена Леонидовна", "кафедра"),
        LecturerInfo("5", "Калинин А.А.", "кафедра"),
        LecturerInfo("6", "Ли С.Н.", "кафедра"),
        LecturerInfo("7", "Лисицын П.П.", "кафедра"),
        LecturerInfo("8", "Петров  В.М.", "кафедра")
    )

    @Test fun petrov_ab_does_not_pull_other_petrovs() {
        val lessons = listOf(Lesson(teacherRaw = "Петров А.Б.; Барт Е.Л."))
        val mine = mine(lessons)
        assertEquals(setOf("1", "4"), mine.map { it.id }.toSet())
    }

    @Test fun short_last_name_is_not_a_substring() {
        val lessons = listOf(Lesson(teacherRaw = "Ли С.Н."))
        val mine = mine(lessons)
        assertEquals(listOf("6"), mine.map { it.id })
    }

    @Test fun collapsed_spaces_still_match() {
        val lessons = listOf(Lesson(teacherRaw = "Петров В.М."))
        val mine = mine(lessons)
        assertEquals(listOf("8"), mine.map { it.id })
    }

    @Test fun homonym_keeps_only_the_lecturer_who_teaches_the_group() {
        val mineLect = LecturerInfo("1331", "Хомелева Р.А.", "")
        val other = LecturerInfo("2137", "Хомелева Р.А.", "Б4")
        val catalog = listOf(mineLect, other)
        val byId = mapOf(
            "1331" to listOf(LecturerLesson(groups = listOf(GroupRef("3313", "А863С")))),
            "2137" to listOf(LecturerLesson(groups = listOf(GroupRef("999", "А161С"))))
        )
        val lessons = listOf(Lesson(teacherRaw = "Хомелева Р.А."))
        val ids = TeacherMatch.myIds(lessons, catalog, { byId[it].orEmpty() }, "3313", "А863С")
        val mine = catalog.filter { TeacherMatch.inMineList(it, ids) }
        assertEquals(listOf("1331"), mine.map { it.id })
    }

    @Test fun homonym_matches_group_number_when_id_is_the_name() {
        val mineLect = LecturerInfo("1331", "Хомелева Р.А.", "")
        val other = LecturerInfo("2137", "Хомелева Р.А.", "Б4")
        val catalog = listOf(mineLect, other)
        val byId = mapOf(
            "1331" to listOf(LecturerLesson(groups = listOf(GroupRef("3313", "А863С")))),
            "2137" to listOf(LecturerLesson(groups = listOf(GroupRef("999", "А161С"))))
        )
        val ids = TeacherMatch.myIds(
            listOf(Lesson(teacherRaw = "Хомелева Р.А.")),
            catalog,
            { byId[it].orEmpty() },
            "А863С",
            "А863С"
        )
        assertEquals(setOf("1331"), catalog.filter { TeacherMatch.inMineList(it, ids) }.map { it.id }.toSet())
    }

    private fun mine(lessons: List<Lesson>): List<LecturerInfo> {
        val ids = TeacherMatch.myIds(lessons, catalog)
        return catalog.filter { TeacherMatch.inMineList(it, ids) }
    }
}
