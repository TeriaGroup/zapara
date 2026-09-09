package ru.bgtu_voenmeh.zapara.ui.widgets

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.GROUP_FIXTURE
import ru.bgtu_voenmeh.zapara.data.GroupParser
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.Schedule
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileDescriptor
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

    @Test fun guest_monday_shows_local_lessons_in_russian() {
        val snap = build(now = LocalDateTime.of(2026, 9, 7, 10, 0))
        assertEquals("Расписание", snap.title)
        assertEquals("Гость · А863С", snap.subtitle)
        assertTrue(snap.empty == null)
        assertEquals(2, snap.rows.size)
        assertEquals("Матан", snap.rows[0].name)
        assertEquals("09:00 – 10:35 · 493 ГК", snap.rows[0].meta)
        assertFalse(snap.rows[0].isPast)
        assertEquals("ОСН РОС ГОС", snap.rows[1].name)
        assertTrue(WidgetJobs.accept(snap.identity, guestId))
    }

    @Test fun after_last_lesson_smart_start_moves_to_tuesday() {
        val snap = build(now = LocalDateTime.of(2026, 9, 14, 14, 31))
        assertEquals(1, snap.rows.size)
        assertTrue(snap.rows[0].name.contains("ФК"))
        assertTrue(snap.rows.none { it.name == "Матан" })
        assertTrue(snap.subtitle.contains("А863С"))
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
