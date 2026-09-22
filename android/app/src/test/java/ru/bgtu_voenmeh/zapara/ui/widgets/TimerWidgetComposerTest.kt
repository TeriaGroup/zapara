package ru.bgtu_voenmeh.zapara.ui.widgets

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileDescriptor
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import java.time.LocalDate
import java.time.LocalDateTime

class TimerWidgetComposerTest {
    private val day = LocalDate.of(2026, 9, 15)
    private val guestId = WidgetJobIdentity.of(ProfileDescriptor.guest(), 0)
    private val settings = ScheduleRepository.SettingsState(
        myGroupId = "3313",
        periodStart = LocalDate.of(2026, 9, 1),
        weekCount = 2
    )

    private fun lesson(index: Int, start: String, end: String, name: String) = Lesson(
        groupId = "3313",
        dayOfWeek = 2,
        parity = 0,
        index = index,
        timeStart = start,
        timeEnd = end,
        subjectRaw = name,
        subjectNormalized = name,
        typeRaw = name.substringBefore(" "),
        roomRaw = "100",
        buildingRaw = "УЛК",
        classroomRaw = "100;"
    )

    private fun bells() = listOf(
        lesson(1, "09:00", "10:35", "пр ИН. ЯЗ."),
        lesson(2, "10:50", "12:25", "пр ОСН РОС ГОС"),
        lesson(3, "12:40", "14:15", "лек ВВЕД В СПЕЦ"),
        lesson(4, "14:55", "16:30", "пр ВЫСШ. МАТЕМАТ")
    )

    private fun at(
        hour: Int,
        minute: Int,
        second: Int = 0,
        lessons: List<Lesson> = bells(),
        prefs: ScheduleRepository.SettingsState = settings,
        whenDay: LocalDate = day,
        cleared: Boolean = false
    ) = TimerWidgetComposer.fromTimer(
        identity = guestId,
        settings = prefs,
        allLessons = lessons,
        now = LocalDateTime.of(whenDay.year, whenDay.month, whenDay.dayOfMonth, hour, minute, second),
        displayName = { LessonFormat.stripType(it.subjectRaw, it.typeRaw) },
        copy = WidgetCopy,
        cleared = cleared
    )

    @Test fun pair_counts_down_to_its_bell() {
        val snap = at(9, 40)
        assertEquals(TimerPhaseKind.Lesson, snap.kind)
        assertEquals("55:00", snap.timeText)
        assertEquals("Пара", snap.phaseText)
        assertEquals("ИН. ЯЗ.", snap.subject)
        assertEquals("100 УЛК", snap.detail)
        assertEquals(55f / 95f, snap.fraction, 0.0001f)
        assertEquals(LocalDateTime.of(day, java.time.LocalTime.of(10, 35)), snap.endsAt)
        assertEquals(LocalDateTime.of(day, java.time.LocalTime.of(9, 41)), snap.nextRefreshAt)
        val started = at(9, 0)
        assertEquals("1:35:00", started.timeText)
        assertEquals(1f, started.fraction, 0.0001f)
        assertEquals(LocalDateTime.of(day, java.time.LocalTime.of(9, 1)), started.nextRefreshAt)
    }

    @Test fun break_counts_down_to_the_next_pair() {
        val snap = at(10, 40)
        assertEquals(TimerPhaseKind.Break, snap.kind)
        assertEquals("10:00", snap.timeText)
        assertEquals("Перемена", snap.phaseText)
        assertEquals("ОСН РОС ГОС", snap.subject)
        assertEquals("в 10:50", snap.detail)
        assertEquals(10f / 15f, snap.fraction, 0.0001f)
        assertEquals(LocalDateTime.of(day, java.time.LocalTime.of(10, 50)), snap.endsAt)
        assertEquals(LocalDateTime.of(day, java.time.LocalTime.of(10, 41)), snap.nextRefreshAt)
        val lunch = at(14, 20)
        assertEquals(TimerPhaseKind.Break, lunch.kind)
        assertEquals("35:00", lunch.timeText)
        assertEquals("ВЫСШ. МАТЕМАТ", lunch.subject)
        assertEquals("в 14:55", lunch.detail)
        assertEquals(35f / 40f, lunch.fraction, 0.0001f)
    }

    @Test fun before_the_first_pair_is_not_a_break() {
        val snap = at(8, 0)
        assertEquals(TimerPhaseKind.Waiting, snap.kind)
        assertEquals("", snap.timeText)
        assertEquals("Сейчас нет пары", snap.phaseText)
        assertEquals("Далее · ИН. ЯЗ. · 09:00", snap.subject)
        assertEquals(0f, snap.fraction, 0.0001f)
        assertNull(snap.endsAt)
        assertEquals(LocalDateTime.of(day, java.time.LocalTime.of(9, 0)), snap.nextRefreshAt)
    }

    @Test fun after_the_last_pair_the_day_is_over() {
        val snap = at(16, 30)
        assertEquals(TimerPhaseKind.Finished, snap.kind)
        assertEquals("Пары закончились", snap.phaseText)
        assertEquals("", snap.timeText)
        assertNull(snap.endsAt)
        assertEquals(day.plusDays(1).atStartOfDay(), snap.nextRefreshAt)
    }

    @Test fun partial_minute_rounds_the_face_and_wakes_at_the_bell() {
        val snap = at(10, 34, 30)
        assertEquals(TimerPhaseKind.Lesson, snap.kind)
        assertEquals("00:30", snap.timeText)
        assertEquals(30f / (95f * 60f), snap.fraction, 0.0001f)
        assertEquals(LocalDateTime.of(day, java.time.LocalTime.of(10, 35)), snap.nextRefreshAt)
    }

    @Test fun overlapping_pair_stays_until_the_later_end() {
        val lessons = bells() + lesson(5, "09:00", "11:20", "лек ДОЛГАЯ")
        val snap = at(10, 40, lessons = lessons)
        assertEquals(TimerPhaseKind.Lesson, snap.kind)
        assertEquals("ДОЛГАЯ", snap.subject)
        assertEquals("40:00", snap.timeText)
        assertEquals(40f / 140f, snap.fraction, 0.0001f)
        assertEquals(LocalDateTime.of(day, java.time.LocalTime.of(11, 20)), snap.endsAt)
    }

    @Test fun blank_or_earlier_end_lasts_ninety_five_minutes() {
        val blank = at(9, 0, lessons = listOf(lesson(1, "09:00", "", "пр ИН. ЯЗ.")))
        val backwards = at(9, 0, lessons = listOf(lesson(1, "09:00", "08:00", "пр ИН. ЯЗ.")))
        assertEquals(TimerPhaseKind.Lesson, blank.kind)
        assertEquals("1:35:00", blank.timeText)
        assertEquals("1:35:00", backwards.timeText)
        assertEquals(LocalDateTime.of(day, java.time.LocalTime.of(10, 35)), blank.endsAt)
    }

    @Test fun sunday_no_group_and_cleared_do_not_invent_a_countdown() {
        val sunday = at(12, 0, whenDay = LocalDate.of(2026, 9, 13))
        assertEquals(TimerPhaseKind.EmptyDay, sunday.kind)
        assertEquals("Пар нет", sunday.phaseText)
        assertEquals("", sunday.timeText)
        val noGroup = at(9, 40, prefs = settings.copy(myGroupId = null))
        assertEquals(TimerPhaseKind.NoGroup, noGroup.kind)
        assertEquals("Группа не выбрана", noGroup.phaseText)
        assertNull(noGroup.nextRefreshAt)
        val cleared = at(9, 40, cleared = true)
        assertTrue(cleared.cleared)
        assertEquals("", cleared.phaseText)
        assertEquals("", cleared.subject)
        assertNull(cleared.nextRefreshAt)
        assertEquals(guestId, cleared.identity)
    }

    @Test fun clock_text_and_shared_refresh_pick_the_sooner_instant() {
        assertEquals("1:35:00", TimerWidgetComposer.clockText(95L * 60))
        assertEquals("55:00", TimerWidgetComposer.clockText(55L * 60))
        assertEquals("00:30", TimerWidgetComposer.clockText(30))
        assertEquals("", TimerWidgetComposer.clockText(0))
        val pairEnd = LocalDateTime.of(day, java.time.LocalTime.of(10, 35))
        val nextMinute = LocalDateTime.of(day, java.time.LocalTime.of(10, 1))
        assertEquals(nextMinute, earlierRefresh(pairEnd, nextMinute))
        assertEquals(pairEnd, earlierRefresh(pairEnd, null))
        assertNull(earlierRefresh(null, null))
    }
}
