package ru.bgtu_voenmeh.zapara.ui.homework

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class HomeworkUndoDoneTest {
    @Test fun undo_applies_only_to_same_owner_group_and_expected_saved_value() {
        val undo = HomeworkUndoDone(42, false, "А863С", "profile-a")
        assertTrue(undo.canApply("profile-a", "А863С", true))
        assertFalse(undo.canApply("profile-b", "А863С", true))
        assertFalse(undo.canApply("profile-a", "И123С", true))
        assertFalse(undo.canApply("profile-a", "А863С", false))
        assertFalse(undo.canApply("profile-a", "А863С", null))
    }
}
