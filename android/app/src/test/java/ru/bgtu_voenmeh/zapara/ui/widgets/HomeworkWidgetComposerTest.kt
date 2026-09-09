package ru.bgtu_voenmeh.zapara.ui.widgets

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Homework
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileDescriptor
import java.time.LocalDate

class HomeworkWidgetComposerTest {
    private val key = "A".repeat(64)
    private val userA = "11111111-1111-4111-8111-111111111111"
    private val userB = "22222222-2222-4222-8222-222222222222"
    private val today = LocalDate.of(2026, 9, 8)
    private val guestId = WidgetJobIdentity.of(ProfileDescriptor.guest(), 0)
    private val math = Lesson(
        groupId = "3313", dayOfWeek = 1, subjectRaw = "лек ВЫСШ. МАТЕМАТ",
        subjectNormalized = "лек высш. математика", typeRaw = "лек"
    )

    private fun hw(
        id: Long,
        text: String,
        status: String,
        due: LocalDate?,
        norm: String = math.subjectNormalized
    ) = Homework(
        id = id, norm = norm, text = text, createdAt = LocalDate.of(2026, 9, 1),
        n = 1, due = due, status = status, done = status == "done"
    )

    private fun build(
        identity: WidgetJobIdentity = guestId,
        items: List<Homework>,
        groupId: String? = "3313",
        groupName: String? = "А863С",
        cleared: Boolean = false
    ) = HomeworkWidgetComposer.fromHomework(
        identity = identity,
        settings = ScheduleRepository.SettingsState(myGroupId = groupId),
        homework = items,
        lessons = listOf(math),
        today = today,
        groupName = groupName,
        displayName = { if (it == math.subjectNormalized) "Матан" else it },
        copy = WidgetCopy,
        cleared = cleared
    )

    @Test fun guest_widget_shows_local_incomplete_homework() {
        val snap = build(
            items = listOf(
                hw(1, "локальный параграф", "burning", today.plusDays(1)),
                hw(2, "уже сдано", "done", today.minusDays(2))
            )
        )
        assertEquals("Домашка", snap.title)
        assertEquals("Гость · А863С", snap.subtitle)
        assertEquals(1, snap.rows.size)
        assertEquals("Матан", snap.rows[0].subject)
        assertTrue(snap.rows[0].detail.contains("локальный параграф"))
        assertEquals("warn", snap.rows[0].tone)
        assertTrue(snap.rows.none { it.detail.contains("уже сдано") })
        assertTrue(WidgetJobs.accept(snap.identity, guestId))
    }

    @Test fun overdue_and_burning_come_before_later() {
        val snap = build(
            items = listOf(
                hw(1, "позже", "far", LocalDate.of(2026, 9, 21)),
                hw(2, "горит сегодня", "burning_urgent", today),
                hw(3, "просрочено", "overdue", LocalDate.of(2026, 9, 5))
            )
        )
        assertEquals(listOf("просрочено", "горит сегодня", "позже"), snap.rows.map { row ->
            row.detail.substringBefore(" · ")
        })
        assertEquals(listOf("bad", "warn", "text2"), snap.rows.map { it.tone })
    }

    @Test fun empty_and_no_group() {
        assertEquals("Домашки нет", build(items = emptyList()).empty)
        val noGroup = build(items = emptyList(), groupId = null, groupName = null)
        assertEquals("Группа не выбрана", noGroup.empty)
        assertTrue(noGroup.rows.isEmpty())
    }

    @Test fun switch_to_b_rejects_cached_a_and_cleared_has_no_a_text() {
        val a = WidgetJobIdentity.of(ProfileDescriptor.account(key, userA), 1)
        val b = WidgetJobIdentity.of(ProfileDescriptor.account(key, userB), 2)
        val cachedA = build(identity = a, items = listOf(hw(9, "secret-account-A-homework", "far", today.plusDays(5))))
        assertTrue(cachedA.rows.any { it.detail.contains("secret-account-A-homework") })
        assertFalse(WidgetJobs.accept(cachedA.identity, b))
        val cleared = HomeworkWidgetComposer.cleared(b, WidgetCopy)
        assertTrue(cleared.cleared)
        assertEquals(b, cleared.identity)
        assertTrue(cleared.rows.isEmpty())
        assertTrue(cleared.rows.none { it.detail.contains("secret-account-A-homework") })
        assertEquals("Домашка", cleared.title)
        val loadedB = build(identity = b, items = listOf(hw(3, "задание Б", "approaching", today.plusDays(3))), groupName = "09С31")
        assertTrue(loadedB.rows.any { it.detail.contains("задание Б") })
        assertTrue(loadedB.rows.none { it.detail.contains("secret-account-A-homework") })
        assertEquals("09С31", loadedB.subtitle)
    }

    @Test fun caps_visible_rows() {
        val many = (1..8).map { hw(it.toLong(), "t$it", "far", today.plusDays(it.toLong())) }
        val snap = build(items = many)
        assertEquals(HomeworkWidgetComposer.MAX_ROWS, snap.rows.size)
    }
}
