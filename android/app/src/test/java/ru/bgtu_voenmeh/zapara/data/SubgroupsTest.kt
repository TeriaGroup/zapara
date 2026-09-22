package ru.bgtu_voenmeh.zapara.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class SubgroupsTest {
    private fun lesson(
        day: Int,
        time: String,
        teacher: String,
        subject: String = "пр ИН. ЯЗ.",
        norm: String = "ин. яз.",
        parity: Int = 0,
        index: Int = 1,
        room: String = "100;"
    ) = Lesson(
        groupId = "3313",
        dayOfWeek = day,
        parity = parity,
        index = index,
        timeStart = time,
        timeEnd = "10:35",
        subjectRaw = subject,
        subjectNormalized = norm,
        teacherRaw = teacher,
        classroomRaw = room
    )

    @Test fun two_teachers_at_one_bell_are_a_choice_and_the_other_row_hides() {
        val ivanov = lesson(1, "09:00", "Иванов И.И.", room = "101;")
        val petrov = lesson(1, "09:00", "Петров П.П.", index = 2, room = "202;")
        val later = lesson(3, "12:40", "Иванов И. И.", index = 1, room = "101;")
        val laterOther = lesson(3, "12:40", "Петров П.П.", index = 2, room = "202;")
        val all = listOf(ivanov, petrov, later, laterOther)
        val index = Subgroups.index(all)
        assertEquals(1, index.streams.size)
        assertEquals(listOf("иванов и и", "петров п п"), index.streams[0].options.map { it.id })
        assertEquals(false, index.streams[0].joined)
        val chosen = mapOf(index.streams[0].id to "иванов и и")
        val left = Subgroups.visible(all, chosen)
        assertEquals(listOf(ivanov, later), left)
        val day = listOf(ivanov, petrov)
        val mark = Subgroups.mark(ivanov, day, index, emptyMap())
        assertEquals(true, mark?.showChooser)
        assertNull(mark?.chosenId)
        assertEquals(false, Subgroups.mark(petrov, day, index, emptyMap())?.showChooser)
    }

    @Test fun one_card_with_two_teachers_stays_and_offers_both() {
        val both = lesson(1, "09:00", "Иванов И.И.; Петров П.П.")
        val index = Subgroups.index(listOf(both))
        assertEquals(1, index.streams.size)
        assertTrue(index.streams[0].joined)
        assertEquals(2, index.streams[0].options.size)
        val chosen = mapOf(index.streams[0].id to index.streams[0].options[0].id)
        assertEquals(listOf(both), Subgroups.visible(listOf(both), chosen))
    }

    @Test fun odd_and_even_teachers_are_not_in_the_room_together() {
        val odd = lesson(1, "09:00", "Иванов И.И.", parity = 1)
        val even = lesson(1, "09:00", "Петров П.П.", parity = 2, index = 1)
        assertTrue(Subgroups.index(listOf(odd, even)).streams.isEmpty())
    }

    @Test fun every_week_lesson_splits_with_the_odd_week_partner() {
        val always = lesson(1, "09:00", "Иванов И.И.", parity = 0)
        val odd = lesson(1, "09:00", "Петров П.П.", parity = 1, index = 2, room = "202;")
        val index = Subgroups.index(listOf(always, odd))
        assertEquals(1, index.streams.size)
        val chosen = mapOf(index.streams[0].id to "петров п п")
        assertEquals(listOf(odd), Subgroups.visible(listOf(always, odd), chosen))
    }

    @Test fun a_shared_lecture_stays_when_only_the_practice_splits() {
        val lecture = lesson(1, "09:00", "Сидоров С.С.", subject = "лек ФИЗИКА", norm = "физика", room = "1;")
        val labA = lesson(1, "12:40", "Иванов И.И.", subject = "лаб ФИЗИКА", norm = "физика", index = 2, room = "2;")
        val labB = lesson(1, "12:40", "Петров П.П.", subject = "лаб ФИЗИКА", norm = "физика", index = 3, room = "3;")
        val all = listOf(lecture, labA, labB)
        val index = Subgroups.index(all)
        val chosen = mapOf(index.streams.single().id to "иванов и и")
        assertEquals(listOf(lecture, labA), Subgroups.visible(all, chosen))
    }

    @Test fun a_choice_for_a_teacher_who_left_the_timetable_shows_both_again() {
        val a = lesson(1, "09:00", "Иванов И.И.")
        val b = lesson(1, "09:00", "Петров П.П.", index = 2, room = "202;")
        val stale = mapOf(Subgroups.index(listOf(a, b)).streams.single().id to "ушел у.у.")
        assertEquals(listOf(a, b), Subgroups.visible(listOf(a, b), stale))
    }

    @Test fun different_subjects_at_one_bell_choose_a_whole_lesson() {
        val history = lesson(2, "10:50", "Иванов И.И.", subject = "лек ИСТОРИЯ", norm = "история", room = "1;")
        val sport = lesson(2, "10:50", "Петров П.П.", subject = "пр ФК", norm = "фк", index = 2, room = "2;")
        val index = Subgroups.index(listOf(history, sport))
        val stream = index.streams.single()
        assertTrue(stream.id.startsWith("t:"))
        val chosen = mapOf(stream.id to stream.options.first { it.label.contains("ФК") }.id)
        assertEquals(listOf(sport), Subgroups.visible(listOf(history, sport), chosen))
    }
}
