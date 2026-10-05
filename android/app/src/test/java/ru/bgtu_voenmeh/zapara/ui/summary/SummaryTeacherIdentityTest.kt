package ru.bgtu_voenmeh.zapara.ui.summary

import org.junit.Assert.assertEquals
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.GroupRef
import ru.bgtu_voenmeh.zapara.data.LecturerInfo
import ru.bgtu_voenmeh.zapara.data.LecturerLesson
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.ui.XmlCopy

class SummaryTeacherIdentityTest {
    private val first = LecturerInfo("one", "Иванов Иван Иванович", "Кафедра 1")
    private val second = LecturerInfo("two", "Иванов Илья Игоревич", "Кафедра 2")
    private fun lessons(groupForSecond: Boolean) = { id: String ->
        listOf(LecturerLesson(lecturerId = id, groups = listOf(GroupRef(
            if (id == "one" || groupForSecond) "3313" else "other", "О3313"))))
    }

    @Test fun known_group_teacher_spelling_variants_merge_when_identity_is_unique() {
        val resolver = SummaryTeacherIdentity(listOf(first, second), lessons(false), "3313", null)
        val rows = listOf("Иванов И.И.", "Иванов И. И.").map { raw ->
            Lesson(groupId = "3313", dayOfWeek = 1, parity = 0, subjectRaw = "Предмет",
                teacherRaw = raw)
        }
        val tiles = SummaryComposer.tiles(2, rows, { _, _ -> "Предмет" }, XmlCopy, resolver::resolve)
        assertEquals(listOf(first.name to 2), tiles.byTeacher)
    }

    @Test fun ambiguous_same_initials_remain_separate_raw_buckets() {
        val resolver = SummaryTeacherIdentity(listOf(first, second), lessons(true), "3313", null)
        assertEquals("raw:Иванов И.И." to "Иванов И.И.", resolver.resolve("Иванов И.И."))
        assertEquals("raw:Иванов И. И." to "Иванов И. И.", resolver.resolve("Иванов И. И."))
    }
}
