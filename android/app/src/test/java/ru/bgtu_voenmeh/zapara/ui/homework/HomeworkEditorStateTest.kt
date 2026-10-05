package ru.bgtu_voenmeh.zapara.ui.homework

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.XmlCopy
import ru.bgtu_voenmeh.zapara.data.Homework
import java.time.LocalDate

class HomeworkEditorStateTest {
    private val dueFor: (Int, String) -> LocalDate? = { n, _ ->
        LocalDate.of(2026, 9, 14).plusWeeks((n - 1).toLong())
    }

    private fun state(text: String = "прочитать", n: Int = 1) = HomeworkEditorState(
        id = null, subjectRaw = "лек ВЫСШ. МАТЕМАТ", subjectDisplay = "Матан",
        text = text, n = n, isEdit = false, dueFor = dueFor
    )

    @Test fun can_save_requires_text() {
        assertFalse(state(text = "").canSave)
        assertFalse(state(text = "   ").canSave)
        assertTrue(state(text = "прочитать главу 2").canSave)
    }

    @Test fun n_does_not_go_below_one() {
        assertEquals(1, state(n = 1).dec().n)
        assertEquals(2, state(n = 1).inc().n)
        assertEquals(10, state(n = 10).inc().n)
    }

    @Test fun due_text_uses_supplied_function() {
        assertEquals("Срок: 21.09 (Пн)", state(n = 2).dueText(XmlCopy))
        val monday = HomeworkEditorState(
            id = null, subjectRaw = "x", subjectDisplay = "x",
            text = "t", n = 1, isEdit = false, dueFor = { _, _ -> LocalDate.of(2026, 9, 21) }
        )
        assertEquals("Срок: 21.09 (Пн)", monday.dueText(XmlCopy))
    }

    @Test fun existing_editor_uses_creation_date_not_today_and_text_keeps_due() {
        val created = LocalDate.of(2026, 9, 1)
        val today = LocalDate.of(2026, 9, 12)
        val homework = Homework(7, "матан", "§5", created, 1, created.plusDays(1), "overdue", false)
        val preview = homeworkEditorDueFor(homework, today) { from, n -> from.plusDays(n.toLong()) }
        val editor = HomeworkEditorState(7, "матан", "Матан", "§5", 1, true, preview)
        assertEquals("Срок: 02.09 (Ср)", editor.dueText(XmlCopy))
        assertEquals(editor.dueText(XmlCopy), editor.withText("§6").dueText(XmlCopy))
        assertEquals(created.plusDays(2), editor.inc().dueFor(2, editor.text))
        assertEquals(editor.dueText(XmlCopy), editor.inc().dec().dueText(XmlCopy))
    }

    @Test fun pending_stored_null_is_not_replaced_by_a_guessed_date() {
        val today = LocalDate.of(2026, 9, 12)
        val homework = Homework(7, "матан", "§5", today.minusDays(11), 1, null, "pending", false)
        val preview = homeworkEditorDueFor(homework, today) { from, n -> from.plusDays(n.toLong()) }
        assertEquals(null, preview(1, homework.text))
        assertEquals(today.plusDays(1), homeworkEditorDueFor(null, today) { from, n -> from.plusDays(n.toLong()) }(1, "новое"))
    }

    @Test fun unchanged_editor_does_not_request_domain_update() {
        val today = LocalDate.of(2026, 9, 12)
        val homework = Homework(7, "матан", "§5", today.minusDays(11), 1, null, "pending", false)
        val editor = HomeworkEditorState(7, "матан", "Матан", "§5", 1, true, dueFor = { _, _ -> today })
        assertFalse(editor.hasChanges(homework))
        assertFalse(editor.inc().dec().hasChanges(homework))
        assertTrue(editor.inc().hasChanges(homework))
        assertTrue(editor.withText("§6").hasChanges(homework))
    }
}
