package ru.bgtu_voenmeh.zapara.ui.homework

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ManualSubjectTest {
    @Test fun manual_entry_requires_no_usable_picker_subject_and_nonblank_normalized_name() {
        assertFalse(manualSubjectAllowed(emptyList(), " \t "))
        assertTrue(manualSubjectAllowed(emptyList(), "  Общая физика  "))
        assertFalse(manualSubjectAllowed(listOf(SubjectUi("Физика", "физика", "Физика")), "Химия"))
    }
}
