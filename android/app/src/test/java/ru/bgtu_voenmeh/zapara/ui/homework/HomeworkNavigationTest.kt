package ru.bgtu_voenmeh.zapara.ui.homework

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import java.time.LocalDate
import java.time.LocalDateTime

class HomeworkNavigationTest {
    @Test fun next_lesson_skips_finished_today_and_keeps_target_time() {
        val context = SchedCtx("3313", LocalDate.of(2026, 9, 1), 2, false)
        val lessons = listOf(Lesson(groupId = "3313", dayOfWeek = 2, parity = 0,
            timeStart = "09:00", timeEnd = "10:35", subjectRaw = "Физика",
            subjectNormalized = "физика"))
        val target = nextHomeworkLesson(lessons, context, "Физика",
            LocalDateTime.of(2026, 9, 8, 11, 0))
        assertEquals(LocalDate.of(2026, 9, 15), target?.date)
        assertEquals("09:00", target?.time)
        assertNull(nextHomeworkLesson(lessons, context, "Математика",
            LocalDateTime.of(2026, 9, 8, 11, 0)))
    }
}
