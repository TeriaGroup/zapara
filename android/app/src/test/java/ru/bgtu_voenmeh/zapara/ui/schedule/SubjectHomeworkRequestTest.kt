package ru.bgtu_voenmeh.zapara.ui.schedule

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SubjectHomeworkRequestTest {
    private val lesson = LessonUi(0, "", "", "", "Математика", null, "", "", "", null,
        emptyList(), emptyList(), false, "Математика", "математика")

    @Test fun stale_subject_result_cannot_replace_another_sheet_or_owner() {
        val request = SubjectHomeworkRequest(7, "profile-a", "А863С", lesson.subjectNorm)
        assertTrue(request.matches(7, "profile-a", "А863С", lesson))
        assertFalse(request.matches(8, "profile-a", "А863С", lesson))
        assertFalse(request.matches(7, "profile-b", "А863С", lesson))
        assertFalse(request.matches(7, "profile-a", "И123С", lesson))
        assertFalse(request.matches(7, "profile-a", "А863С", lesson.copy(subjectNorm = "история")))
        assertFalse(request.matches(7, "profile-a", "А863С", null))
    }
}
