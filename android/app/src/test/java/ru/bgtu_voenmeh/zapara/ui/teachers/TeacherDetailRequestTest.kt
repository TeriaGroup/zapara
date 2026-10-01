package ru.bgtu_voenmeh.zapara.ui.teachers

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class TeacherDetailRequestTest {
    private val teacher = TeacherRowUi("t-1", "Преподаватель", "Математика", true)

    @Test fun old_detail_cannot_reopen_closed_or_replaced_teacher() {
        val request = TeacherDetailRequest(7, teacher.id)
        assertTrue(request.matches(7, teacher))
        assertFalse(request.matches(8, teacher))
        assertFalse(request.matches(7, null))
        assertFalse(request.matches(7, teacher.copy(id = "t-2")))
    }

    @Test fun same_teacher_late_result_cannot_publish_after_group_or_profile_epoch_changes() {
        val request = TeacherDetailRequest(4, teacher.id, "group-a", "profile-a", 8)
        assertTrue(request.matches(4, teacher, "group-a", "profile-a", 8))
        assertFalse(request.matches(4, teacher, "group-b", "profile-a", 9))
        assertFalse(request.matches(4, teacher, "group-a", "profile-b", 8))
        assertFalse(request.matches(4, teacher, "group-a", "profile-a", 9))
    }
}
