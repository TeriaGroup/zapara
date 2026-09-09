package ru.bgtu_voenmeh.zapara.ui.homework

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Homework
import ru.bgtu_voenmeh.zapara.ui.XmlCopy
import java.time.LocalDate

class HomeworkGroupsTest {
    private val today = LocalDate.of(2026, 9, 8)

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
        assertEquals("сдано", HomeworkGroups.dueLabel(due.copy(status = "done", done = true), today, XmlCopy))
        assertEquals(
            "просрочено с 05.09",
            HomeworkGroups.dueLabel(due.copy(status = "overdue", due = LocalDate.of(2026, 9, 5)), today, XmlCopy)
        )
    }
}
