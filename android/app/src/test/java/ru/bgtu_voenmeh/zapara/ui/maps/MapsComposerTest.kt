package ru.bgtu_voenmeh.zapara.ui.maps

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.GROUP_FIXTURE
import ru.bgtu_voenmeh.zapara.data.GroupParser
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import ru.bgtu_voenmeh.zapara.data.Schedule
import ru.bgtu_voenmeh.zapara.ui.XmlCopy
import java.time.LocalDate
import java.time.LocalDateTime

class MapsComposerTest {
    private val parsed by lazy { GroupParser.parse(GROUP_FIXTURE) }
    private val ctx = SchedCtx("3313", LocalDate.of(2026, 9, 1), 2, false)

    private fun lessonsOn(date: LocalDate) =
        Schedule.lessonsForDate(parsed.lessons, ctx.groupId, date, ctx.periodStart, ctx.weekCount, ctx.invert)

    @Test fun floors_by_building() {
        assertEquals((1..4).toList(), MapsComposer.floors("ГК"))
        assertEquals((1..5).toList(), MapsComposer.floors("УЛК"))
    }

    @Test fun context_line_going_next_tomorrow_and_none() {
        val lesson = Lesson(
            timeStart = "09:00", timeEnd = "10:35",
            roomRaw = "493", buildingRaw = "ГК", classroomRaw = "493;"
        )
        val nowGoing = LocalDateTime.of(2026, 9, 7, 9, 30)
        assertEquals(
            "Идёт пара · 493 ГК · до 10:35",
            MapsComposer.contextLine(nowGoing, listOf(lesson), null, XmlCopy)
        )
        val nowNext = LocalDateTime.of(2026, 9, 7, 8, 20)
        assertEquals(
            "Следующая пара · 493 ГК · через 40 мин",
            MapsComposer.contextLine(nowNext, listOf(lesson), LocalDate.of(2026, 9, 7) to lesson, XmlCopy)
        )
        val nowEve = LocalDateTime.of(2026, 9, 7, 20, 0)
        assertEquals(
            "Следующая пара · завтра 09:00 · 493 ГК",
            MapsComposer.contextLine(nowEve, emptyList(), LocalDate.of(2026, 9, 8) to lesson, XmlCopy)
        )
        assertEquals("Нет предстоящих занятий", MapsComposer.contextLine(nowEve, emptyList(), null, XmlCopy))
    }

    @Test fun next_lesson_skips_sunday_and_finds_monday() {
        val saturday = LocalDateTime.of(2026, 9, 12, 18, 0)
        val next = MapsComposer.nextLesson(saturday, ::lessonsOn, horizonDays = 14)
        assertEquals(LocalDate.of(2026, 9, 14), next?.first)
        assertEquals("лек ВЫСШ. МАТЕМАТ", next?.second?.subjectRaw)
        val far = LocalDateTime.of(2026, 10, 1, 12, 0)
        assertNull(MapsComposer.nextLesson(far, { emptyList() }, horizonDays = 2))
    }
}
