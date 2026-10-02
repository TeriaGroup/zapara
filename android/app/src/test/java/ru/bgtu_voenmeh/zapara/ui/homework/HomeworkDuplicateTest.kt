package ru.bgtu_voenmeh.zapara.ui.homework

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Homework
import java.time.LocalDate

class HomeworkDuplicateTest {
    @Test fun warning_requires_same_subject_text_and_due_and_can_be_overridden() {
        val due = LocalDate.of(2026, 10, 9)
        val old = Homework(7, "математика", "Решить № 1", LocalDate.of(2026, 10, 1),
            1, due, "pending", false)
        val draft = HomeworkEditorState(null, "Математика", "Математика", " решить № 1 ",
            1, false, { _, _ -> due })
        assertTrue(duplicateHomework(listOf(old), draft))
        assertFalse(duplicateHomework(listOf(old), draft.copy(duplicateApproved = true)))
        assertFalse(duplicateHomework(listOf(old), draft.copy(id = 7, isEdit = true)))
        assertFalse(duplicateHomework(listOf(old), draft.copy(dueFor = { _, _ -> due.plusDays(1) })))
        assertFalse(duplicateHomework(listOf(old), draft.copy(text = "Другое задание")))
    }
}
