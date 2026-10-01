package ru.bgtu_voenmeh.zapara.ui.homework

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class HomeworkBrowseTest {
    private val rows = listOf(
        HomeworkGroupUi(GroupStatus.Overdue, "Просрочено", listOf(
            HomeworkItemUi(1, "Высшая математика", "Решить задачи", "", "overdue", false),
            HomeworkItemUi(2, "История", "Прочитать главу", "", "overdue", false)), false),
        HomeworkGroupUi(GroupStatus.Done, "Сдано", listOf(
            HomeworkItemUi(3, "Математика", "Прочитать конспект", "", "done", true)), true)
    )

    @Test fun counts_cover_all_rows_while_query_and_completion_filter_visible_groups() {
        val result = HomeworkBrowse.filter(rows, "  ПРОЧИТАТЬ  ", HomeworkCompletionFilter.Active)
        assertEquals(2, result.totalActive)
        assertEquals(1, result.totalDone)
        assertEquals(listOf(2L), result.groups.flatMap { it.items }.map { it.id })
        assertEquals(1, result.visibleCount)
        assertFalse(result.groups.any { it.status == GroupStatus.Done })
    }

    @Test fun done_filter_keeps_status_group_and_empty_can_reset() {
        val done = HomeworkBrowse.filter(rows, "математика конспект", HomeworkCompletionFilter.Done)
        assertEquals(listOf(GroupStatus.Done), done.groups.map { it.status })
        assertEquals(listOf(3L), done.groups.single().items.map { it.id })
        assertTrue(HomeworkBrowse.filter(rows, "отсутствует", HomeworkCompletionFilter.All).groups.isEmpty())
        assertEquals(3, HomeworkBrowse.filter(rows, "", HomeworkCompletionFilter.All).visibleCount)
    }

    @Test fun raw_subject_is_searchable() {
        val raw = rows.first().copy(items = listOf(rows.first().items.first().copy(subjectRaw = "ФКиС")))
        assertEquals(listOf(1L), HomeworkBrowse.filter(listOf(raw), "фкис задачи", HomeworkCompletionFilter.Active)
            .groups.flatMap { it.items }.map { it.id })
    }

    @Test fun reset_clears_search_and_exposes_completed_only_data() {
        val state = HomeworkUiState(loaded = true, hasGroup = true, groups = rows,
            browseQuery = "нет", browseFilter = HomeworkCompletionFilter.Active).resetBrowse()
        assertEquals("", state.browseQuery)
        assertEquals(HomeworkCompletionFilter.All, state.browseFilter)
        assertEquals(3, HomeworkBrowse.filter(state.groups, state.browseQuery, state.browseFilter).visibleCount)
    }

    @Test fun group_change_restores_default_filter_without_dropping_other_state() {
        val undo = HomeworkUndoDone(1, false, "А863С", "profile-a")
        val before = HomeworkUiState(loaded = true, hasGroup = true, groups = rows, guest = true,
            browseQuery = "история", browseFilter = HomeworkCompletionFilter.Done, undoDone = undo)
        val after = before.forGroupChange()
        assertEquals("", after.browseQuery)
        assertEquals(HomeworkCompletionFilter.Active, after.browseFilter)
        assertEquals(null, after.undoDone)
        assertEquals(rows, after.groups)
        assertTrue(after.guest)
    }

    @Test fun shared_rows_obey_the_same_query_and_completion_filters_as_personal_rows() {
        val rows = listOf(
            SharedHomeworkItemUi("a", "community", "Математика", "Решить задачи", "Без срока", false, 0, true, false),
            SharedHomeworkItemUi("b", "community", "История", "Прочитать главу", "Без срока", true, 2, true, true))
        assertEquals(listOf("a"), HomeworkBrowse.shared(rows, "математика задачи", HomeworkCompletionFilter.Active).map { it.id })
        assertEquals(listOf("b"), HomeworkBrowse.shared(rows, "прочитать", HomeworkCompletionFilter.Done).map { it.id })
        assertEquals(rows, HomeworkBrowse.shared(rows, "", HomeworkCompletionFilter.All))
    }
}
