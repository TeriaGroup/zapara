package ru.bgtu_voenmeh.zapara.ui.schedule

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.GROUP_FIXTURE
import ru.bgtu_voenmeh.zapara.data.GroupParser
import ru.bgtu_voenmeh.zapara.data.Homework
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import ru.bgtu_voenmeh.zapara.data.Schedule
import ru.bgtu_voenmeh.zapara.ui.XmlCopy
import java.time.LocalDate
import java.time.LocalDateTime

class ScheduleComposerTest {
    private val parsed by lazy { GroupParser.parse(GROUP_FIXTURE) }
    private val all get() = parsed.lessons
    private val ctx = SchedCtx("3313", LocalDate.of(2026, 9, 1), 2, false)
    private val monday = LocalDate.of(2026, 9, 7)
    private val mathNorm = Parity.normalizeSubject("лек ВЫСШ. МАТЕМАТ")

    private fun page(date: LocalDate, now: LocalDateTime) = ScheduleComposer.page(
        date = date,
        allLessons = all,
        ctx = ctx,
        now = now,
        displayName = { norm, _ -> if (norm == mathNorm) "Матан" else "" },
        homeworkFor = { norm ->
            if (norm == mathNorm) listOf(
                Homework(
                    id = 1, norm = mathNorm, text = "§5, задачи 1–12",
                    createdAt = LocalDate.of(2026, 9, 1), n = 1,
                    due = LocalDate.of(2026, 9, 21), status = "far", done = false
                )
            ) else emptyList()
        },
        friendsFor = { lesson ->
            if (lesson.roomRaw == "493") listOf(
                FriendDotUi(index = 0, groupName = "09С31", members = "Иван", score = 100, hint = "Иван · 09С31 · та же аудитория")
            ) else emptyList()
        },
        copy = XmlCopy
    )

    @Test fun monday_cards_rename_past_homework_and_friends() {
        val page = page(monday, LocalDateTime.of(2026, 9, 7, 12, 0))
        assertEquals(2, page.lessons.size)
        val first = page.lessons[0]
        assertEquals("Матан", first.name)
        assertEquals("ВЫСШ. МАТЕМАТ", first.original)
        assertTrue(first.isPast)
        assertEquals("493 ГК", first.room)
        assertEquals("лекция", first.type)
        assertEquals("срок 21.09 (Пн)", first.homework.single().label)
        assertEquals("Иван · 09С31 · та же аудитория", first.friends.single().hint)
        val second = page.lessons[1]
        assertFalse(second.isPast)
        assertEquals("практика", second.type)
        assertEquals("563 УЛК", second.room)
    }

    @Test fun caption_uses_parity_and_week_number() {
        val page = page(monday, LocalDateTime.of(2026, 9, 7, 12, 0))
        assertEquals("Понедельник, 7 сентября · нечётная неделя · 1-я неделя", page.caption)
    }

    @Test fun empty_thursday_has_next_lesson_hint() {
        val page = page(LocalDate.of(2026, 9, 10), LocalDateTime.of(2026, 9, 10, 12, 0))
        assertTrue(page.lessons.isEmpty())
        assertFalse(page.isSunday)
        assertEquals("следующая пара — 14 сентября, Матан", page.nextHint)
        assertEquals(
            LocalDate.of(2026, 9, 14),
            Schedule.nextOccurrenceBySubject(all, "3313", mathNorm, LocalDate.of(2026, 9, 10), ctx.periodStart, ctx.weekCount, ctx.invert)
        )
    }

    @Test fun sunday_is_marked_without_hint() {
        val page = page(LocalDate.of(2026, 9, 13), LocalDateTime.of(2026, 9, 13, 12, 0))
        assertTrue(page.isSunday)
        assertNull(page.nextHint)
    }

    @Test fun pager_range_is_731_pages_with_today_at_365() {
        val today = LocalDate.of(2026, 9, 8)
        assertEquals(365, ScheduleComposer.pageIndex(today, today))
        assertEquals(today.minusDays(365), ScheduleComposer.dateAt(0, today))
        assertEquals(today.plusDays(365), ScheduleComposer.dateAt(730, today))
        assertEquals(731, ScheduleComposer.PAGE_COUNT)
    }
}
