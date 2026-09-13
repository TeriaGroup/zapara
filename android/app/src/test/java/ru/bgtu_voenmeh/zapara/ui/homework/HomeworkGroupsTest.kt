package ru.bgtu_voenmeh.zapara.ui.homework

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Homework
import ru.bgtu_voenmeh.zapara.ui.XmlCopy
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import java.time.LocalDate

class HomeworkGroupsTest {
    private val today = LocalDate.of(2026, 9, 12)

    private fun hw(id: Long, status: String, due: LocalDate? = today.plusDays(id)) = Homework(
        id = id, norm = "n$id", text = "t$id", createdAt = LocalDate.of(2026, 9, 1),
        n = 1, due = due, status = status, done = status == "done"
    )

    @Test fun groups_five_buckets_in_order_and_omits_empty() {
        val items = listOf(
            hw(1, "overdue", LocalDate.of(2026, 9, 5)),
            hw(2, "burning_urgent", today),
            hw(3, "burning", today.plusDays(1)),
            hw(4, "approaching", today.plusDays(4)),
            hw(5, "far", LocalDate.of(2026, 9, 21)),
            hw(6, "done", LocalDate.of(2026, 9, 3))
        ).map { HomeworkGroups.toItem(it, "Предмет", today, XmlCopy) }
        val groups = HomeworkGroups.group(items, XmlCopy)
        assertEquals(
            listOf(GroupStatus.Overdue, GroupStatus.Burning, GroupStatus.Soon, GroupStatus.Later, GroupStatus.Done),
            groups.map { it.status }
        )
        assertEquals(listOf("Просрочено", "Горит", "Скоро", "Дальше", "Сдано"), groups.map { it.title })
        assertEquals(2, groups.first { it.status == GroupStatus.Burning }.items.size)
        assertEquals(2L, groups.first { it.status == GroupStatus.Burning }.items.first().id)
        assertTrue(groups.first { it.status == GroupStatus.Done }.collapsed)
        assertEquals(emptyList<HomeworkGroupUi>(), HomeworkGroups.group(emptyList(), XmlCopy))
    }

    @Test fun due_labels() {
        val due = Homework(
            1, "n", "t", LocalDate.of(2026, 9, 1), 1,
            LocalDate.of(2026, 9, 21), "far", false
        )
        assertEquals("срок 21.09 (Пн)", HomeworkGroups.dueLabel(due, today, XmlCopy))
        assertEquals("срок 21.09 (Пн)", HomeworkGroups.dueLabel(due.copy(status = "done", done = true), today, XmlCopy))
        assertEquals(
            "срок 05.09 (Сб)",
            HomeworkGroups.dueLabel(due.copy(status = "overdue", due = LocalDate.of(2026, 9, 5)), today, XmlCopy)
        )
    }

    @Test fun date_is_independent_of_every_status_and_completion() {
        val statuses = listOf("overdue", "burning_urgent", "burning", "approaching", "pending", "done", "far", "unknown")
        for (due in listOf(today.minusDays(1), today, today.plusDays(1), null)) {
            val expected = due?.let { XmlCopy.get("hw_due", LessonFormat.dayMonth(it), LessonFormat.weekdayShort(it, XmlCopy)) }
                ?: XmlCopy.get("hw_due_none")
            for (status in statuses) for (done in listOf(false, true)) {
                val homework = hw(1, status, due).copy(done = done)
                assertEquals("$due/$status/$done", expected, HomeworkGroups.dueLabel(homework, today, XmlCopy))
                assertEquals(expected, HomeworkGroups.dueLabel(homework, today.plusYears(1), XmlCopy))
            }
        }
    }

    @Test fun status_is_localized_independent_of_date_with_done_priority() {
        val labels = mapOf("overdue" to "Просрочено", "burning_urgent" to "Горит сегодня", "burning" to "Горит",
            "approaching" to "Скоро", "pending" to "Позже", "done" to "Сдано", "far" to "Позже", "unknown" to "Позже")
        labels.forEach { (status, label) ->
            assertEquals(label, HomeworkGroups.statusLabel(status, false, XmlCopy))
            assertEquals("Сдано", HomeworkGroups.statusLabel(status, true, XmlCopy))
            for (due in listOf(today.minusDays(1), today, today.plusDays(1), null)) {
                val item = HomeworkGroups.toItem(hw(1, status, due), "Матан", today, XmlCopy)
                assertEquals(label, item.statusLabel)
                assertEquals(due, item.due)
                assertEquals(status, item.status)
            }
        }
    }

    @Test fun later_bucket_orders_by_due_date_not_label() {
        val items = listOf(
            hw(1, "far", LocalDate.of(2026, 10, 1)),
            hw(2, "far", LocalDate.of(2026, 9, 9))
        ).map { HomeworkGroups.toItem(it, "Предмет", today, XmlCopy) }
        val later = HomeworkGroups.group(items, XmlCopy).single { it.status == GroupStatus.Later }.items
        assertEquals(listOf(2L, 1L), later.map { it.id })
    }
}
