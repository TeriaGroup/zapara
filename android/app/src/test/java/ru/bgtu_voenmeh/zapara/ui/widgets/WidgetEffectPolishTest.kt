package ru.bgtu_voenmeh.zapara.ui.widgets

import org.junit.Assert.*
import org.junit.Test
import kotlin.math.abs

class WidgetEffectPolishTest {
    @Test fun read_errors_keep_static_retry_content_instead_of_starting_a_scene() {
        val identity = WidgetJobIdentity("guest", "local", 1)
        val policy = WidgetMotionPolicy.of(true, 1f, true)
        val row = ScheduleWidgetRow("Физика", "09:00", false, 1)
        val schedule = ScheduleWidgetSnapshot(identity, "Расписание", "", null, listOf(row))
        val updated = schedule.copy(rows = listOf(row.copy(name = "Математика")))
        assertNull(WidgetRowEffects.schedule(schedule.copy(readError = "Ошибка"), updated, policy))
        assertNull(WidgetRowEffects.schedule(schedule, updated.copy(readError = "Ошибка"), policy))
        val task = HomeworkWidgetRow("Физика", "Читать", "text2", 1)
        val homework = HomeworkWidgetSnapshot(identity, "Домашка", "", null, listOf(task))
        assertNull(WidgetRowEffects.homework(homework, homework.copy(rows = emptyList(), readError = "Ошибка"), setOf(1), policy))
        val room = WayfinderWidgetSnapshot(identity, "Куда идти", "Сейчас", "Физика", "09:00", "201", "201", null, true, null)
        assertNull(WidgetFaceEffects.room(room, room.copy(room = "302", readError = "Ошибка"), policy))
        val timer = TimerWidgetSnapshot(identity, "01:00", "Пара", "Физика", "201", TimerPhaseKind.Lesson, 0.5f)
        assertNull(WidgetFaceEffects.timer(timer, timer.copy(subject = "Математика", readError = "Ошибка"), policy))
        val date = java.time.LocalDate.of(2026, 10, 5)
        val week = WeekWidgetSnapshot(identity, "Неделя", "", (0..6).map { WeekWidgetDay(date.plusDays(it.toLong()), "День", 1, it == 0) }, null)
        assertNull(WidgetFaceEffects.week(week, week.copy(readError = "Ошибка", days = week.days.map { it.copy(lessonCount = 2) }), policy))
        assertNotNull(WidgetRowEffects.schedule(schedule.copy(staleDays = 3), updated.copy(staleDays = 3), policy))
    }

    @Test fun room_direction_tracks_numeric_changes_and_supports_lettered_rooms() {
        assertEquals(1, roomReelDirection("201 Б", "493а ГК"))
        assertEquals(-1, roomReelDirection("493а ГК", "201 Б"))
        assertEquals(1, roomReelDirection("Спортзал", "Дистанционно"))
        assertEquals(1, roomReelDirection("493а", "493б"))
        val forward = directionalRoomReel(0.4f, 1)
        val backward = directionalRoomReel(0.4f, -1)
        assertEquals(forward.oldOffsetYDp, -backward.oldOffsetYDp, 0.001f)
        assertEquals(forward.newOffsetYDp, -backward.newOffsetYDp, 0.001f)
        assertEquals(forward.newAlpha, backward.newAlpha, 0f)
    }

    @Test fun room_settle_is_bounded_and_ends_exactly_without_scaling_text() {
        for (direction in listOf(-1, 1)) {
            val start = directionalRoomReel(0f, direction)
            val end = directionalRoomReel(1f, direction)
            assertEquals(0f, start.oldOffsetYDp, 0f)
            assertEquals(1f, start.oldAlpha, 0f)
            assertEquals(12f * direction, start.newOffsetYDp, 0f)
            assertEquals(0f, end.newOffsetYDp, 0f)
            assertEquals(1f, end.newAlpha, 0f)
            assertEquals(end, directionalRoomReel(Float.NaN, direction))
            (0..100).map { directionalRoomReel(it / 100f, direction) }.forEach {
                assertTrue(abs(it.oldOffsetYDp) <= 12f)
                assertTrue(abs(it.newOffsetYDp) <= 12f)
                assertTrue(it.newAlpha in 0f..1f)
            }
        }
        assertTrue(directionalRoomReel(0.75f, 1).newOffsetYDp < 0f)
    }

    @Test fun week_marker_moves_continuously_across_rows_and_finishes_at_destination() {
        val from = WidgetEffectRect(120f, 100f, 176f, 148f)
        val to = WidgetEffectRect(8f, 40f, 64f, 88f)
        assertEquals(from, travellingWeekMarker(from, to, 0f))
        assertEquals(to, travellingWeekMarker(from, to, 1f))
        assertEquals(to, travellingWeekMarker(from, to, Float.NaN))
        val frames = (0..100).map { travellingWeekMarker(from, to, it / 100f) }
        assertTrue(frames.zipWithNext().all { (a, b) -> b.left <= a.left && b.top <= a.top })
        assertTrue(frames.all { it.right > it.left && it.bottom > it.top })
        assertTrue(frames[40].top < from.top && frames[40].top > to.top)
    }

    @Test fun row_sweep_never_reverses_or_leaks_outside_small_rows() {
        for (width in listOf(1f, 8f, 48f, 280f)) {
            (0..100).forEach {
                val frame = widgetRowSweep(it / 100f, width)
                assertTrue(frame.tail in 0f..width)
                assertTrue(frame.head in frame.tail..width)
                assertTrue(frame.alpha in 0f..1f)
            }
            assertEquals(0f, widgetRowSweep(0f, width).alpha, 0f)
            assertEquals(0f, widgetRowSweep(1f, width).alpha, 0f)
        }
    }

    @Test fun completion_check_draws_once_then_disappears_exactly() {
        val frames = (0..100).map { widgetCompletionCheck(it / 100f) }
        assertEquals(0f, frames.first().alpha, 0f)
        assertEquals(0f, frames.last().alpha, 0f)
        assertTrue(frames.zipWithNext().all { (a, b) -> b.drawFraction >= a.drawFraction })
        assertTrue(frames.all { it.drawFraction in 0f..1f && it.alpha in 0f..1f && abs(it.offsetYDp) <= 1.5f })
        assertEquals(frames.last(), widgetCompletionCheck(Float.NaN))
    }
}
