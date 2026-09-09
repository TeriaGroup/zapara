package ru.bgtu_voenmeh.zapara.ui.homework

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.XmlCopy
import java.time.LocalDate

class HomeworkEditorStateTest {
    private val dueFor: (Int) -> LocalDate? = { n ->
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
            text = "t", n = 1, isEdit = false, dueFor = { LocalDate.of(2026, 9, 21) }
        )
        assertEquals("Срок: 21.09 (Пн)", monday.dueText(XmlCopy))
    }
}
