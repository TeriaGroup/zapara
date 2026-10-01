package ru.bgtu_voenmeh.zapara.ui.week

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.GROUP_FIXTURE
import ru.bgtu_voenmeh.zapara.data.GroupParser
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.ui.XmlCopy
import java.time.LocalDate

class WeekComposerTest {
    private val parsed by lazy { GroupParser.parse(GROUP_FIXTURE) }
    private val today = LocalDate.of(2026, 9, 8)
    private val ctx = SchedCtx("3313", LocalDate.of(2026, 9, 1), 2, false)
    private val mathNorm = Parity.normalizeSubject("лек ВЫСШ. МАТЕМАТ")

    @Test fun odd_week_six_days_and_lesson_counts() {
        val days = WeekComposer.compose(1, parsed.lessons, { norm, _ -> if (norm == mathNorm) "Матан" else "" }, ctx, today, XmlCopy)
        assertEquals(6, days.size)
        // From Tue 08.09 (week 2 even), the next odd Monday is 14.09 (week 3).
        assertEquals("Понедельник · 14.09", days[0].title)
        assertEquals(2, days[0].rows.size)
        assertEquals("лекция", days[0].rows[0].type)
        assertEquals("практика", days[0].rows[1].type)
        assertEquals(0, days.first { it.dow == 4 }.rows.size)
    }

    @Test fun week_rows_keep_a_colored_type_mark_next_to_the_subject() {
        val section = java.io.File("src/main/java/ru/bgtu_voenmeh/zapara/ui/week/WeekSection.kt").readText()
        assertTrue(section.contains("LessonTypeChip"))
        assertTrue(section.contains("row.type"))
        assertTrue(section.contains("Week.Type."))
    }

    @Test fun nearest_current_tuesday_is_today() {
        val current = if (Parity.isOddWeek(today, ctx.periodStart, ctx.weekCount, ctx.invert)) 1 else 2
        assertEquals(2, current)
        assertEquals(today, WeekComposer.nearestDate(2, current, ctx, today))
        val days = WeekComposer.compose(current, parsed.lessons, { _, _ -> "" }, ctx, today, XmlCopy)
        assertTrue(days.first { it.dow == 2 }.isToday)
    }

    @Test fun inverted_odd_lands_on_xml_even_dates() {
        val inverted = ctx.copy(invert = true)
        assertEquals(LocalDate.of(2026, 9, 21), WeekComposer.nearestDate(1, 1, inverted, today))
    }
    @Test fun selected_week_keeps_all_seven_absolute_dates_including_weekend() {
        val selected = LocalDate.of(2026, 9, 19)
        val days = WeekComposer.compose(1, parsed.lessons, { _, _ -> "" }, ctx, today, XmlCopy, selected)
        assertEquals((14L..20L).map { LocalDate.of(2026, 9, it.toInt()) }, days.map { it.date })
        assertEquals(selected, days.first { it.dow == 6 }.date)
    }

    @Test fun overview_exposes_end_time_and_teacher_from_the_same_lesson() {
        val lesson = Lesson(groupId = "3313", dayOfWeek = 2, parity = 0,
            timeStart = "09:00", timeEnd = "10:35", subjectRaw = "Математика",
            subjectNormalized = "математика", teacherRaw = "Иванов И. И.")
        val days = WeekComposer.compose(2, listOf(lesson), { _, _ -> "Математика" }, ctx, today, XmlCopy)
        val row = days.first { it.dow == 2 }.rows.single()
        assertEquals("09:00–10:35", row.time)
        assertEquals("Иванов И. И.", row.teacher)
    }

    @Test fun week_search_finds_teacher_and_room_and_preserves_day_navigation_date() {
        val first = WeekDayUi(1, "Понедельник", LocalDate.of(2026, 9, 7),
            listOf(WeekRowUi("09:00", "Математика", "А-101", teacher = "Иванов")), false)
        val second = WeekDayUi(2, "Вторник", LocalDate.of(2026, 9, 8),
            listOf(WeekRowUi("11:00", "Физика", "Б-202", teacher = "Петров")), true)
        assertEquals(listOf(first), WeekBrowse.filter(listOf(first, second), "ИВАНОВ А-101", false))
        assertEquals(listOf(second), WeekBrowse.filter(listOf(first, second), "б-202", false))
        assertTrue(WeekBrowse.filter(listOf(first, second), "нет", false).isEmpty())
    }

}
