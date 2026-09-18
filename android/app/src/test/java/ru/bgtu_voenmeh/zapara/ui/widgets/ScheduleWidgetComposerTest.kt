package ru.bgtu_voenmeh.zapara.ui.widgets

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.GROUP_FIXTURE
import ru.bgtu_voenmeh.zapara.data.GroupParser
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.Schedule
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileDescriptor
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import java.time.LocalDate
import java.time.LocalDateTime

class ScheduleWidgetComposerTest {
    private val parsed by lazy { GroupParser.parse(GROUP_FIXTURE) }
    private val all get() = parsed.lessons
    private val key = "A".repeat(64)
    private val userA = "11111111-1111-4111-8111-111111111111"
    private val userB = "22222222-2222-4222-8222-222222222222"
    private val mathNorm = Parity.normalizeSubject("лек ВЫСШ. МАТЕМАТ")
    private val periodStart = LocalDate.of(2026, 9, 1)
    private val guestId = WidgetJobIdentity.of(ProfileDescriptor.guest(), 0)
    private val settings = ScheduleRepository.SettingsState(
        myGroupId = "3313",
        periodStart = periodStart,
        weekCount = 2
    )

    private fun displayName(norm: String) = if (norm == mathNorm) "Матан" else ""

    private fun build(
        identity: WidgetJobIdentity = guestId,
        now: LocalDateTime,
        prefs: ScheduleRepository.SettingsState = settings,
        cleared: Boolean = false
    ) = ScheduleWidgetComposer.fromSchedule(
        identity = identity,
        settings = prefs,
        allLessons = all,
        now = now,
        groupName = parsed.groups.first { it.id == "3313" }.name,
        displayName = { lesson -> displayName(lesson.subjectNormalized).ifBlank { "" } },
        copy = WidgetCopy,
        cleared = cleared
    )

    @Test fun rows_for_height_fit_small_and_tall_widgets() {
        assertEquals(1, ScheduleWidgetComposer.rowsForHeightDp(80))
        assertEquals(2, ScheduleWidgetComposer.rowsForHeightDp(160))
        assertEquals(4, ScheduleWidgetComposer.rowsForHeightDp(280))
        assertEquals(ScheduleWidgetComposer.MAX_ROWS, ScheduleWidgetComposer.rowsForHeightDp(400))
    }

    @Test fun small_widget_slides_to_later_pairs_after_the_visible_ones_end() {
        val day = LocalDate.of(2026, 9, 15)
        val lessons = (1..4).map { i ->
            val starts = listOf("09:00", "10:50", "12:40", "14:55")
            val ends = listOf("10:35", "12:25", "14:15", "16:30")
            val names = listOf("пр ИН. ЯЗ.", "пр ОСН РОС ГОС", "лек ВВЕД В СПЕЦ", "пр ВЫСШ. МАТЕМАТ")
            Lesson(
                groupId = "3313", dayOfWeek = 2, parity = 0, index = i,
                timeStart = starts[i - 1], timeEnd = ends[i - 1],
                subjectRaw = names[i - 1], subjectNormalized = Parity.normalizeSubject(names[i - 1]),
                typeRaw = names[i - 1].substringBefore(" "), roomRaw = "100", buildingRaw = "УЛК", classroomRaw = "100;"
            )
        }
        fun at(hour: Int, minute: Int, capacity: Int = 2) = ScheduleWidgetComposer.fromSchedule(
            identity = guestId,
            settings = settings,
            allLessons = lessons,
            now = LocalDateTime.of(day.year, day.month, day.dayOfMonth, hour, minute),
            groupName = "Н162С",
            displayName = { LessonFormat.stripType(it.subjectRaw, it.typeRaw) },
            copy = WidgetCopy,
            capacity = capacity
        )
        assertEquals(listOf("ИН. ЯЗ.", "ОСН РОС ГОС"), at(9, 0).rows.map { it.name })
        assertEquals(listOf("ВВЕД В СПЕЦ", "ВЫСШ. МАТЕМАТ"), at(12, 30).rows.map { it.name })
        assertEquals(listOf("ВЫСШ. МАТЕМАТ"), at(16, 0).rows.map { it.name })
        assertEquals(LocalDateTime.of(day, java.time.LocalTime.of(10, 35)), at(9, 0).nextRefreshAt)
        assertEquals(LocalDateTime.of(day, java.time.LocalTime.of(14, 15)), at(12, 30).nextRefreshAt)
    }

    @Test fun guest_monday_shows_local_lessons_in_russian() {
        val snap = build(now = LocalDateTime.of(2026, 9, 14, 10, 0))
        assertEquals("Расписание", snap.title)
        assertEquals("Гость · А863С", snap.subtitle)
        assertTrue(snap.empty == null)
        assertEquals(2, snap.rows.size)
        assertEquals("Матан", snap.rows[0].name)
        assertEquals("09:00 – 10:35 · 493 ГК", snap.rows[0].meta)
        assertFalse(snap.rows[0].isPast)
        assertEquals("ОСН РОС ГОС", snap.rows[1].name)
        assertTrue(WidgetJobs.accept(snap.identity, guestId))
        assertEquals(LocalDateTime.of(2026, 9, 14, 10, 35), snap.nextRefreshAt)
    }

    @Test fun after_last_lesson_smart_start_moves_to_tuesday() {
        val snap = build(now = LocalDateTime.of(2026, 9, 14, 14, 31))
        assertEquals(1, snap.rows.size)
        assertTrue(snap.rows[0].name.contains("ФК"))
        assertTrue(snap.rows.none { it.name == "Матан" })
        assertTrue(snap.subtitle.contains("А863С"))
        assertEquals(LocalDateTime.of(2026, 9, 15, 0, 0), snap.nextRefreshAt)
    }

    @Test fun no_group_uses_empty_copy_and_keeps_identity() {
        val snap = build(
            now = LocalDateTime.of(2026, 9, 7, 10, 0),
            prefs = settings.copy(myGroupId = null)
        )
        assertEquals("Группа не выбрана", snap.empty)
        assertTrue(snap.rows.isEmpty())
        assertEquals(guestId, snap.identity)
    }

    @Test fun empty_day_says_no_lessons() {
        val snap = build(now = LocalDateTime.of(2026, 9, 10, 12, 0))
        assertTrue(snap.rows.isEmpty())
        assertEquals("Пар нет", snap.empty)
    }

    @Test fun cleared_snapshot_for_b_drops_account_a_lessons() {
        val a = WidgetJobIdentity.of(ProfileDescriptor.account(key, userA), 1)
        val b = WidgetJobIdentity.of(ProfileDescriptor.account(key, userB), 2)
        val cachedA = build(identity = a, now = LocalDateTime.of(2026, 9, 7, 10, 0))
        assertTrue(cachedA.rows.any { it.name == "Матан" })
        assertFalse(WidgetJobs.accept(cachedA.identity, b))
        val cleared = build(identity = b, now = LocalDateTime.of(2026, 9, 7, 10, 0), cleared = true)
        assertTrue(cleared.cleared)
        assertTrue(cleared.rows.isEmpty())
        assertTrue(cleared.rows.none { it.name.contains("Матан") })
        assertEquals("Расписание", cleared.title)
        assertEquals(b, cleared.identity)
    }
}
