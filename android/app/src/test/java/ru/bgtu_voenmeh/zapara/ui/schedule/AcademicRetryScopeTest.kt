package ru.bgtu_voenmeh.zapara.ui.schedule

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class AcademicRetryScopeTest {
    @Test fun delayed_retry_error_cannot_replace_newer_same_group_success_or_aba_group() {
        val request = AcademicRetryScope(4, 8, "profile-a", "group-a")
        assertTrue(request.matches(4, 8, "profile-a", "group-a"))
        assertFalse(request.matches(4, 9, "profile-a", "group-a"))
        assertFalse(request.matches(4, 10, "profile-a", "group-a"))
        assertFalse(request.matches(4, 8, "profile-a", "group-b"))
        assertFalse(request.matches(4, 8, "profile-b", "group-a"))
        assertFalse(request.matches(5, 8, "profile-a", "group-a"))
    }
}
