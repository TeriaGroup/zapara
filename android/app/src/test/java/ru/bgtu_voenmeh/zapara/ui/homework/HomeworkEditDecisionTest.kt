package ru.bgtu_voenmeh.zapara.ui.homework

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class HomeworkEditDecisionTest {
    private fun editor(id: Long? = null) = HomeworkEditorState(
        id, "Математика", "Математика", "", 1, id != null,
        { _, _ -> null }, draft = "draft-a"
    )

    @Test fun pristine_idle_editor_can_be_replaced_but_same_target_stays_open() {
        val blankAdd = editor()
        assertEquals(HomeworkEditDecision.ReplacePristine, homeworkEditDecision(blankAdd, 42))
        assertTrue(homeworkEditStillAllowed(blankAdd, blankAdd, 42))
        assertEquals(HomeworkEditDecision.AlreadyOpen, homeworkEditDecision(editor(42), 42))
        assertEquals(HomeworkEditDecision.Open, homeworkEditDecision(null, 42))
    }

    @Test fun changed_busy_or_partly_saved_editor_blocks_new_target() {
        val blankAdd = editor()
        assertEquals(HomeworkEditDecision.Blocked, homeworkEditDecision(blankAdd.withText("Новый текст"), 42))
        assertEquals(HomeworkEditDecision.Blocked, homeworkEditDecision(blankAdd.copy(work = HomeworkEditorWork.Saving), 42))
        assertEquals(HomeworkEditDecision.Blocked, homeworkEditDecision(blankAdd.copy(persistedId = 7), 42))
    }

    @Test fun editor_change_during_target_validation_cannot_be_discarded() {
        val before = editor()
        assertFalse(homeworkEditStillAllowed(before, before.withText("Введено во время загрузки"), 42))
        assertFalse(homeworkEditStillAllowed(before, before.copy(), 42))
        assertFalse(homeworkEditStillAllowed(null, editor(), 42))
    }
}
