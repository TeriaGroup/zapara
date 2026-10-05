package ru.bgtu_voenmeh.zapara.ui.homework

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SubjectPickerScopeTest {
    @Test fun old_picker_cannot_be_reused_after_a_to_b_to_a_or_owner_change() {
        val picker = SubjectPickerUi(listOf(SubjectUi("Математика", "математика", "Математика")),
            groupId = "group-a", profileName = "profile-a", groupEpoch = 7)
        assertTrue(picker.matches("group-a", "profile-a", 7))
        assertFalse(picker.matches("group-b", "profile-a", 8))
        assertFalse(picker.matches("group-a", "profile-a", 9))
        assertFalse(picker.matches("group-a", "profile-b", 7))
    }
}
