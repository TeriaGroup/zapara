package ru.bgtu_voenmeh.zapara.ui.week

import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Homework
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import java.time.LocalDate

class WeekHomeworkProjectionTest {
    private val created = LocalDate.of(2026, 10, 1)
    private val stale = LocalDate.of(2026, 10, 2)
    private val actual = LocalDate.of(2026, 10, 7)
    private val ctx = SchedCtx("group", LocalDate.of(2026, 9, 1), 2, false)
    private val task = Homework(1, "math", "Task", created, 1, stale, "soon", false)
    private val selectedLessons = listOf(Lesson(groupId = "group", dayOfWeek = 3, parity = 0,
        subjectRaw = "Math", subjectNormalized = "math", timeStart = "09:00", timeEnd = "10:30"))
    @Test fun direct_week_projection_uses_selected_snapshot_without_homework_viewmodel_or_database_recompute() {
        val result = projectWeekHomework(listOf(task, task.copy(id = 2, done = true)), selectedLessons,
            ctx, setOf(stale, actual)) { norm, _ -> norm }
        assertEquals(actual, result.deadlines.first { it.id == 1L }.date)
        assertEquals(stale, result.deadlines.first { it.id == 2L }.date)
        assertEquals(stale, task.due)
    }
    @Test fun missing_current_lessons_do_not_place_a_stale_active_deadline() {
        val result = projectWeekHomework(listOf(task), emptyList(), ctx, setOf(stale)) { norm, _ -> norm }
        assertTrue(result.deadlines.isEmpty())
        assertEquals(1, result.undated)
    }
}
