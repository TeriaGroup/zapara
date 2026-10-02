package ru.bgtu_voenmeh.zapara.ui.homework

import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Homework
import java.time.LocalDate

class HomeworkRescheduleTest {
    private val before = Homework(1,"math","Task",LocalDate.of(2026,10,1),1,LocalDate.of(2026,10,2),"soon",false)
    private val row = HomeworkRescheduleRow(before,"Math",LocalDate.of(2026,10,9))
    @Test fun postponement_requires_real_later_date_and_remaining_occurrence_capacity() {
        assertTrue(row.eligible)
        assertFalse(row.copy(afterDue=null).eligible)
        assertFalse(row.copy(afterDue=before.due).eligible)
        assertFalse(row.copy(before=before.copy(n=10)).eligible)
        assertFalse(row.copy(before=before.copy(done=true)).eligible)
    }
    @Test fun applying_and_undoing_reject_changes_to_text_deadline_or_completion() {
        assertTrue(row.matches(before)); assertFalse(row.matches(before.copy(text="Changed")))
        val applied=before.copy(n=2,due=row.afterDue)
        assertTrue(row.canUndo(applied)); assertFalse(row.canUndo(applied.copy(done=true)))
        assertFalse(row.canUndo(applied.copy(due=LocalDate.of(2026,10,10))))
        assertFalse(row.canUndo(before)); assertFalse(row.matches(null))
    }
    @Test fun changed_projection_during_write_throws_so_the_enclosing_transaction_rolls_back() {
        var current = before
        val failure = runCatching {
            applyVerifiedReschedule(row, false, { current }, { row.afterDue }, { _, n ->
                // Represents a subgroup selection changing between the pre-check and updateCore.
                current = current.copy(n = n, due = row.afterDue!!.plusDays(1))
            })
        }.exceptionOrNull()
        assertTrue(failure is HomeworkRescheduleConflict)
    }
    @Test fun verified_write_and_conditional_undo_keep_the_exact_preview_dates() {
        var current = before
        assertTrue(applyVerifiedReschedule(row, false, { current }, { row.afterDue }, { text, n ->
            current = current.copy(text = text, n = n, due = row.afterDue)
        }))
        assertTrue(applyVerifiedReschedule(row, true, { current }, { before.due }, { text, n ->
            current = current.copy(text = text, n = n, due = before.due)
        }))
        assertEquals(before, current)
    }
}
