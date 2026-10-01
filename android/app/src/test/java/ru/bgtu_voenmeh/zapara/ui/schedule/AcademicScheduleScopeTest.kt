package ru.bgtu_voenmeh.zapara.ui.schedule

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.db.OverrideEntity

class AcademicScheduleScopeTest {
    @Test fun personal_undo_requires_exact_profile_group_and_acknowledged_value() {
        val undo = ScheduleCompletionUndo(7, previousDone = false, profileName = "owner-a", groupId = "group-a")
        assertTrue(undo.canApply("owner-a", "group-a", true))
        assertFalse(undo.canApply("owner-b", "group-a", true))
        assertFalse(undo.canApply("owner-a", "group-b", true))
        assertFalse(undo.canApply("owner-a", "group-a", false))
        assertFalse(undo.canApply("owner-a", "group-a", null))
    }

    @Test fun reset_of_weekday_override_keeps_global_and_other_weekday() {
        fun row(id: Long, norm: String, scope: String) = OverrideEntity(id, norm, scope, "Новое", null, "2026-10-01")
        val rows = listOf(row(1, "математика", "global"), row(2, "математика", "weekday:1"),
            row(3, "математика", "weekday:3"), row(4, "физика", "weekday:1"))
        assertEquals(listOf(2L), renameResetIds(rows, "математика", renameScopeKey(1, 1)))
        assertEquals(listOf(1L), renameResetIds(rows, "математика", renameScopeKey(0, 1)))
    }

    @Test fun shared_fetch_reconciles_new_rows_without_dropping_local_or_duplicating_shared() {
        val local = HomeworkRowUi(9, "Личная", "Завтра", "active", false)
        val old = HomeworkRowUi(0, "Старая", "", "active", false, sharedId = "a")
        val fresh = HomeworkRowUi(0, "Новая", "", "active", false, sharedId = "b")
        val rows = reconcileSubjectSheetRows(listOf(local, old), listOf(fresh, fresh))
        assertEquals(listOf(local, fresh), rows)
    }
}
