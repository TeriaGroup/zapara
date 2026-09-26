package ru.bgtu_voenmeh.zapara.ui.schedule

import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Homework
import java.time.*

class DayPlannerTest {
    private val date = LocalDate.of(2026, 9, 26)
    private fun lesson(start: String, end: String) = LessonUi(1, start, end, "", "Subject", null, "", "", "", null, emptyList(), emptyList(), false, "Subject", "subject")
    @Test fun overlap_is_merged_before_long_breaks_are_calculated() {
        val breaks = ScheduleComposer.breaks(listOf(lesson("09:00", "11:00"), lesson("10:00", "12:00"), lesson("12:20", "13:00"), lesson("14:00", "15:00")))
        assertEquals(1, breaks.size)
        assertEquals(LocalTime.of(13, 0), breaks.single().start)
        assertEquals(60, breaks.single().minutes)
    }
    @Test fun exactly_thirty_minutes_is_a_break_and_shorter_intervals_are_not() {
        assertEquals(30, ScheduleComposer.breaks(listOf(lesson("09:00", "10:00"), lesson("10:30", "11:00"))).single().minutes)
        assertTrue(ScheduleComposer.breaks(listOf(lesson("09:00", "10:00"), lesson("10:29", "11:00"))).isEmpty())
    }
    @Test fun malformed_and_reversed_intervals_do_not_create_free_time() {
        assertTrue(ScheduleComposer.breaks(listOf(lesson("bad", "11:00"), lesson("12:00", "10:00"))).isEmpty())
    }
    @Test fun deadline_window_includes_three_absolute_days_and_undated_day_subjects() {
        fun hw(id: Long, norm: String, due: LocalDate?) = Homework(id, norm, "task", date, 1, due, "active", false)
        val rows = listOf(hw(1, "other", date.minusDays(1)), hw(2, "other", date), hw(3, "other", date.plusDays(2)), hw(4, "other", date.plusDays(3)), hw(5, "subject", null), hw(6, "other", null))
        assertEquals(listOf(2L, 3L, 5L), ScheduleComposer.deadlines(date, setOf("subject"), rows).map { it.id })
    }
    @Test fun featured_uses_actual_clock_today_first_future_and_no_past_feature() {
        val first = lesson("09:00", "10:35"); val second = lesson("12:00", "13:35")
        val page = DayPage(date, true, "", listOf(first, second), null, false)
        assertEquals(second, ScheduleComposer.featured(page, date.atTime(11, 0)))
        assertNull(ScheduleComposer.featured(page, date.atTime(14, 0)))
        assertEquals(first, ScheduleComposer.featured(page.copy(date = date.plusDays(1)), date.atTime(14, 0)))
        assertNull(ScheduleComposer.featured(page.copy(date = date.minusDays(1)), date.atTime(8, 0)))
    }
    @Test fun midnight_never_changes_selected_absolute_day() {
        assertEquals(date.plusDays(1) to date, ScheduleComposer.syncToday(date.plusDays(1), date, date))
        assertEquals(date.plusDays(1) to date.plusDays(2), ScheduleComposer.syncToday(date.plusDays(1), date, date.plusDays(2)))
    }
    @Test fun shared_revocation_redacts_all_surfaces_without_touching_guest_schedule_or_local_work() {
        val personal = HomeworkRowUi(1, "Personal", "", "active", false)
        val shared = HomeworkRowUi(0, "Private", "", "active", false, "shared-id")
        val page = DayPage(date, false, "Public", listOf(lesson("09:00", "10:35")), null, false, deadlines = listOf(personal, shared))
        val state = ScheduleUiState(loaded = true, hasGroup = true, selected = date, pages = mapOf(date to page), subjectRows = listOf(personal, shared), sharedDetail = shared, undoShared = "shared-id" to false)
        val cleared = ScheduleComposer.purgeShared(state)
        assertEquals(listOf(personal), cleared.pages[date]!!.deadlines); assertEquals(page.lessons, cleared.pages[date]!!.lessons)
        assertEquals(listOf(personal), cleared.subjectRows); assertNull(cleared.sharedDetail); assertNull(cleared.undoShared); assertEquals(date, cleared.selected)
    }

    @Test fun rendered_cached_days_rederive_today_and_past_at_midnight_and_lesson_boundary() {
        val tomorrow = date.plusDays(1)
        val cached = DayPage(tomorrow, false, "", listOf(lesson("09:00", "10:35")), null, false)
        val rendered = ScheduleComposer.atClock(cached, tomorrow.atTime(10,35))
        assertTrue(rendered.isToday); assertTrue(rendered.lessons.single().isPast)
        val yesterday = ScheduleComposer.atClock(cached.copy(isToday = true), tomorrow.plusDays(1).atStartOfDay())
        assertFalse(yesterday.isToday); assertFalse(yesterday.lessons.single().isPast)
    }
    @Test fun editor_preview_and_private_creation_use_one_frozen_future_anchor() {
        val anchor = date.plusDays(5)
        val editor = ru.bgtu_voenmeh.zapara.ui.homework.HomeworkEditorState(null, "Math", "Math", "Task", 1, false, { n,_ -> anchor.plusDays(n.toLong()) }, anchorDate = anchor)
        assertEquals(anchor, editor.creationAnchor(date)); assertEquals(anchor, editor.creationAnchor(date.plusDays(1)))
        assertEquals(editor.creationAnchor(date).plusDays(1), editor.dueFor(editor.n, editor.text))
    }

    @Test fun changed_schedule_or_group_cannot_silently_save_another_due_date() {
        val editor=ru.bgtu_voenmeh.zapara.ui.homework.HomeworkEditorState(null,"Math","Math","Task",1,false,{ _,_ -> date.plusDays(5) },anchorDate=date.plusDays(2),scheduleGroupId="g1")
        assertTrue(editor.matchesSaveContext("g1",date.plusDays(5)))
        assertFalse(editor.matchesSaveContext("g2",date.plusDays(5))); assertFalse(editor.matchesSaveContext("g1",date.plusDays(6)))
    }

    @Test fun next_clock_tick_is_aligned_to_midnight_or_minute_boundary() {
        assertEquals(25_000,ScheduleComposer.millisUntilNextMinute(date.atTime(23,59,35)))
        assertEquals(60_000,ScheduleComposer.millisUntilNextMinute(date.atTime(12,0)))
    }

}
