package ru.bgtu_voenmeh.zapara.ui.week

import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.*
import ru.bgtu_voenmeh.zapara.data.campus.*
import java.time.LocalDate

class AcademicAgendaTest {
    private val monday = LocalDate.of(2026, 12, 28)
    private val ctx = SchedCtx("a", monday, 2, false)
    private fun lesson(type: String = "экз", room: String = "320") = Lesson(groupId = "a", dayOfWeek = 1,
        timeStart = "09:00", timeEnd = "10:30", subjectRaw = "Математика", subjectNormalized = "математика",
        typeRaw = type, teacherRaw = "Преподаватель", classroomRaw = room)

    @Test fun assessments_are_nearest_actual_dates_per_raw_slot_and_do_not_confuse_same_bell_rooms() {
        val rows = assessmentAgenda(listOf(lesson(), lesson(room = "321"), lesson(type = "лек")), ctx, monday)
        assertEquals(2, rows.size)
        assertTrue(rows.all { it.date == monday })
        assertNotEquals(academicLessonKey(rows[0].lesson), academicLessonKey(rows[1].lesson))
        val next = assessmentAgenda(listOf(lesson()), ctx, monday.plusDays(1))
        assertEquals(monday.plusWeeks(1), next.single().date)
    }
    @Test fun assessment_types_are_narrow_and_preperiod_is_unknown_not_an_exam_event() {
        assertFalse(isAssessment(lesson("дифференциальные уравнения")))
        assertFalse(isAssessment(lesson("контрольная")))
        assertTrue(isAssessment(lesson("диф. зачёт")))
        assertTrue(isAssessment(lesson("").copy(subjectRaw = "экз. Математика")))
        assertTrue(assessmentAgenda(listOf(lesson()), ctx.copy(periodStart = monday.plusDays(28)), monday).isEmpty())
    }
    @Test fun room_agenda_resolves_building_identity_and_states_cached_group_coverage() {
        val graph = CampusGraph(1, listOf("ГК", "УЛК"), listOf(
            Node("gk320", "room", "ГК", 3, 0.0, 0.0, room = "320"),
            Node("ulk320", "room", "УЛК", 3, 0.0, 0.0, room = "320")), emptyList())
        val agenda = roomAgenda(graph, listOf(GroupInfo("a", "А") to listOf(lesson(), lesson(room = "320*"), lesson(room = "неизвестно")),
            GroupInfo("empty", "Без копии") to emptyList()), ctx, monday)
        assertEquals(listOf("А"), agenda.groups)
        assertEquals(setOf("gk320", "ulk320"), agenda.rows.map { it.nodeId }.toSet())
        assertEquals(1, agenda.unmatched)
        assertTrue(roomAgenda(graph, listOf(GroupInfo("a", "А") to listOf(lesson())), ctx, monday.minusDays(1)).unknown)
    }
    @Test fun refresh_comparison_requires_changed_source_same_owner_context_and_actual_week() {
        val row = WeekRowUi("09:00–10:30", "Имя", "320", start = "09:00", end = "10:30", subjectRaw = "Математика", classroomRaw = "320")
        val days = (0L..6L).map { WeekDayUi((it + 1).toInt(), "", monday.plusDays(it), if (it == 0L) listOf(row, row) else emptyList(), false) }
        val before = WeekUiState(loaded = true, hasGroup = true, days = days, groupId = "a", profileName = "owner", fetchedAt = "1", sourceContext = "same")
        val after = before.copy(fetchedAt = "2", days = days.map { if (it.dow == 1) it.copy(rows = listOf(row.copy(name = "Переименовано"))) else it })
        assertEquals(1, refreshComparison(before, after)!!.single().removed.size)
        assertNull(refreshComparison(before, after.copy(profileName = "other")))
        assertNull(refreshComparison(before, after.copy(sourceBaseContext = "changed period")))
        assertNotNull(refreshComparison(before, after.copy(fetchedAt = "1", sourceContext = "changed subgroup")))
        assertNull(refreshComparison(before, after.copy(error = "failed")))
        assertNull(refreshComparison(before, after.copy(fetchedAt = "1")))
    }
    @Test fun exact_target_includes_raw_type_and_missing_teacher_matches_display_placeholder() {
        assertNotEquals(academicLessonKey(lesson("экз")), academicLessonKey(lesson("лек")))
        val blank = lesson().copy(teacherRaw = "")
        assertEquals(academicLessonKey(blank), academicLessonKey(blank.index, blank.timeStart, blank.timeEnd,
            blank.subjectRaw, "—", blank.classroomRaw, blank.typeRaw))
        assertTrue(agendaTime("9:00") < agendaTime("10:00"))
    }
}
