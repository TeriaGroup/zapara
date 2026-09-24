package ru.bgtu_voenmeh.zapara.ui.widgets

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import java.time.LocalDate
import java.time.LocalDateTime

class WayfinderWidgetComposerTest {
    private val today = LocalDate.of(2026, 9, 23)
    private val guest = WidgetJobIdentity("guest", "guest-test", 7)
    private val settings = ScheduleRepository.SettingsState(myGroupId = "g")

    private fun lesson(
        name: String,
        day: Int = 3,
        start: String = "09:00",
        end: String = "10:35",
        index: Int = 1,
        parity: Int = 0,
        room: String = "493",
        classroom: String = "493;"
    ) = Lesson(
        groupId = "g", dayOfWeek = day, parity = parity, index = index,
        timeStart = start, timeEnd = end, subjectRaw = name,
        roomRaw = room, buildingRaw = "ГК", classroomRaw = classroom
    )

    private fun at(
        now: LocalDateTime,
        lessons: List<Lesson>,
        prefs: ScheduleRepository.SettingsState = settings
    ) = WayfinderWidgetComposer.fromSchedule(
        guest, prefs, lessons, now, { it.subjectRaw }, WidgetCopy, false
    )

    @Test fun active_lesson_wins_over_next_room() {
        val current = lesson("Математика")
        val next = lesson("Физика", start = "10:50", end = "12:25", index = 2, room = "201")
        val snap = at(today.atTime(10, 0), listOf(current, next))

        assertEquals("Математика", snap.subject)
        assertEquals("Сейчас", snap.status)
        assertEquals("09:00 – 10:35", snap.time)
        assertEquals("493 ГК", snap.room)
        assertEquals("493;", snap.classroomRaw)
        assertEquals(today, snap.targetDate)
        assertEquals(today.atTime(10, 35), snap.nextRefreshAt)
        assertTrue(snap.opensMap)
        assertEquals(guest, snap.identity)
    }

    @Test fun overlapping_lessons_choose_the_same_holder_as_timer() {
        val short = lesson("Короткая", end = "10:35", index = 1)
        val longHigherIndex = lesson("Длинная", end = "11:20", index = 3)
        val longLowerIndex = lesson("Победитель", end = "11:20", index = 2)
        val now = today.atTime(10, 0)
        val lessons = listOf(short, longHigherIndex, longLowerIndex)

        val timer = TimerWidgetComposer.fromTimer(guest, settings, lessons, now, { it.subjectRaw }, WidgetCopy)
        val snap = at(now, lessons)

        assertEquals("Победитель", timer.subject)
        assertEquals(timer.subject, snap.subject)
        assertEquals(today.atTime(11, 20), snap.nextRefreshAt)
    }

    @Test fun next_lesson_today_wins_over_tomorrow_and_refreshes_at_start() {
        val tomorrow = lesson("Завтра", day = 4, start = "08:00")
        val laterToday = lesson("Сегодня", start = "12:40", end = "14:15", index = 3)
        val earlierToday = lesson("Скоро", start = "10:50", end = "12:25", index = 2)
        val snap = at(today.atTime(10, 35), listOf(tomorrow, laterToday, earlierToday))

        assertEquals("Скоро", snap.subject)
        assertEquals("Следующая", snap.status)
        assertEquals(today, snap.targetDate)
        assertEquals(today.atTime(10, 50), snap.nextRefreshAt)
    }

    @Test fun overlapping_later_start_wakes_wayfinder_when_timer_changes_holder() {
        val lessons = listOf(
            lesson("A", start = "09:00", end = "10:35", index = 1),
            lesson("B", start = "10:00", end = "11:35", index = 2)
        )
        val before = at(today.atTime(9, 30), lessons)
        assertEquals("A", before.subject)
        assertEquals(today.atTime(10, 0), before.nextRefreshAt)
        val boundary = today.atTime(10, 0)
        val timer = TimerWidgetComposer.fromTimer(guest, settings, lessons, boundary, { it.subjectRaw }, WidgetCopy)
        assertEquals("B", at(boundary, lessons).subject)
        assertEquals(timer.subject, at(boundary, lessons).subject)
    }

    @Test fun current_is_start_inclusive_and_end_exclusive_with_ending_lesson_retained() {
        val lessons = listOf(lesson("A"), lesson("B", start = "10:50", end = "12:25", index = 2))
        assertEquals("Следующая", at(today.atTime(8, 59, 59), lessons).status)
        assertEquals("Сейчас", at(today.atTime(9, 0), lessons).status)
        assertEquals("A", at(today.atTime(10, 34, 59), lessons).subject)
        val ended = at(today.atTime(10, 35), lessons)
        assertEquals("B", ended.subject)
        assertEquals("Следующая", ended.status)
        assertEquals(today.atTime(10, 50), ended.nextRefreshAt)
        assertEquals("Сейчас", at(today.atTime(10, 50), lessons).status)
    }

    @Test fun tomorrow_is_selected_after_last_lesson_today() {
        val snap = at(today.atTime(17, 0), listOf(
            lesson("Прошла", end = "10:35"),
            lesson("Завтра", day = 4, start = "08:00", end = "09:35")
        ))

        assertEquals("Завтра", snap.subject)
        assertEquals(today.plusDays(1), snap.targetDate)
        assertEquals(today.plusDays(1).atTime(8, 0), snap.nextRefreshAt)
    }

    @Test fun seven_day_search_stops_before_the_eighth_date() {
        val nextWeek = today.plusDays(6)
        val selected = lesson("Через шесть дней", day = nextWeek.dayOfWeek.value, parity = 0)
        val outside = lesson("Через семь дней", day = today.dayOfWeek.value, parity = 0)
        val snap = at(today.atTime(18, 0), listOf(selected, outside))

        assertEquals("Через шесть дней", snap.subject)
        assertEquals(nextWeek, snap.targetDate)
        assertEquals(nextWeek.atTime(9, 0), snap.nextRefreshAt)
        val empty = at(today.atTime(18, 0), listOf(outside))
        assertNull(empty.targetDate)
        assertFalse(empty.opensMap)
    }

    @Test fun parity_inversion_changes_the_selected_room() {
        val even = lesson("Чётная", parity = 2, room = "201", classroom = "201")
        val odd = lesson("Нечётная", parity = 1, room = "301", classroom = "301")
        val now = today.atTime(10, 0)

        assertEquals("Чётная", at(now, listOf(even, odd)).subject)
        assertEquals("Нечётная", at(now, listOf(even, odd), settings.copy(parityInvert = true)).subject)
    }

    @Test fun no_group_and_cleared_faces_have_no_stale_lesson() {
        val noGroup = at(today.atTime(10, 0), listOf(lesson("Секрет")), settings.copy(myGroupId = null))
        assertEquals("Группа не выбрана", noGroup.empty)
        assertEquals("", noGroup.subject)
        assertNull(noGroup.targetDate)
        assertNull(noGroup.nextRefreshAt)
        assertEquals(guest, noGroup.identity)

        val cleared = WayfinderWidgetComposer.cleared(guest, WidgetCopy, true)
        assertTrue(cleared.cleared)
        assertTrue(cleared.isDark)
        assertEquals("", cleared.subject)
        assertNull(cleared.targetDate)
        assertNull(cleared.nextRefreshAt)
        assertFalse(cleared.opensMap)
    }

    @Test fun remote_and_malformed_rooms_open_schedule() {
        val now = today.atTime(10, 0)
        val remote = at(now, listOf(lesson("Онлайн", room = "дистанционно", classroom = "дистанционно")))
        assertEquals("дистанционно", remote.room)
        assertFalse(remote.opensMap)
        assertEquals(today, remote.targetDate)

        val malformed = at(now, listOf(lesson("Неясная", room = "где-то", classroom = "где-то")))
        assertFalse(malformed.opensMap)
        assertEquals(today, malformed.targetDate)

        val missing = at(now, listOf(lesson("Без аудитории", room = "", classroom = "")))
        assertFalse(missing.opensMap)
        assertEquals("Аудитория не указана", missing.room)
        assertEquals(today, missing.targetDate)
    }
}
