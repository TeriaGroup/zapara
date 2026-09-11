package ru.bgtu_voenmeh.zapara

import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.UiCopy
import ru.bgtu_voenmeh.zapara.ui.homework.*
import java.time.LocalDate

/** Tests the actual fixture and production editor reducers, not source-string contracts. */
class HomeworkCaptureModelTest {
    private val copy = UiCopy { key, args -> when {
        key.startsWith("weekday_") -> listOf("Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс")[key.last().digitToInt() - 1]
        key == "hw_due" -> "срок ${args[0]} (${args[1]})"
        key == "hw_due_none" -> "срок —"
        key == "hw_due_prefix" -> "Срок: ${args[0]}"
        else -> mapOf("hw_status_overdue" to "Просрочено", "hw_status_urgent" to "Горит сегодня",
            "hw_status_burning" to "Горит", "hw_status_soon" to "Скоро", "hw_status_later" to "Позже",
            "hw_status_done" to "Сдано")[key] ?: key
    } }

    @Test fun actual_fixture_has_literal_order_counts_statuses_dates_and_null() {
        val groups = HomeworkCaptureModel.state(copy).groups
        assertEquals(GroupStatus.entries, groups.map { it.status })
        assertEquals(listOf(listOf(101L), listOf(102L, 103L), listOf(104L), listOf(105L, 106L), listOf(107L)), groups.map { it.items.map { row -> row.id } })
        assertEquals(listOf(1, 2, 1, 2, 1), groups.map { it.items.size })
        assertEquals(listOf(false, false, false, false, true), groups.map { it.collapsed })
        val items = groups.flatMap { it.items }
        assertEquals(listOf("overdue", "burning_urgent", "burning", "approaching", "far", "pending", "done"), items.map { it.status })
        assertEquals(HomeworkCaptureModel.expectedDue, items.map { it.dueLabel })
        assertEquals(HomeworkCaptureModel.expectedStatus, items.map { it.statusLabel })
        assertEquals(listOf(11, 12, 13, 14, 20, null, 10), items.map { it.due?.dayOfMonth })
        assertEquals(listOf(false, false, false, false, false, false, true), items.map { it.done })
    }

    @Test fun text_and_n_preview_use_creation_origin_not_today_and_revert_stored_or_null() {
        listOf(101L, 106L).forEach { id ->
            val original = HomeworkCaptureModel.records.single { it.id == id }
            val editor = HomeworkCaptureModel.editor(id)
            assertEquals(original.due, editor.dueFor(editor.n, editor.text))
            val edited = editor.withText("Изменённый конспект")
            assertTrue(edited.hasChanges(original))
            assertEquals(LocalDate.of(2026, 9, 2), edited.dueFor(edited.n, edited.text))
            assertEquals("Срок: 03.09 (Чт)", edited.inc().dueText(copy))
            assertEquals("Срок: 02.09 (Ср)", edited.inc().dec().dueText(copy))
            val reverted = edited.inc().dec().withText(original.text)
            assertFalse(reverted.hasChanges(original))
            assertEquals(original.due, reverted.dueFor(reverted.n, reverted.text))
            assertEquals(original.due, editor.withText(" ${original.text} ").dueFor(editor.n, " ${original.text} "))
        }
    }

    @Test fun actual_fixture_events_cancel_reopen_and_save_callback_never_mutate_records() {
        val records = HomeworkCaptureModel.records.toList()
        var state = HomeworkCaptureModel.state(copy)
        listOf(101L, 106L).forEach { id ->
            state = HomeworkCaptureModel.reduce(state, HomeworkEvent.Edit(id))
            state = HomeworkCaptureModel.reduce(state, HomeworkEvent.EditorText("Другая запись"))
            state = HomeworkCaptureModel.reduce(state, HomeworkEvent.Inc)
            assertEquals(2, state.editor!!.n)
            state = HomeworkCaptureModel.reduce(state, HomeworkEvent.Dec)
            state = HomeworkCaptureModel.reduce(state, HomeworkEvent.Cancel)
            assertNull(state.editor)
            state = HomeworkCaptureModel.reduce(state, HomeworkEvent.Edit(id))
            assertEquals("Конспект $id", state.editor!!.text)
            state = HomeworkCaptureModel.reduce(state, HomeworkEvent.Save)
            assertNull(state.editor)
        }
        assertEquals(records, HomeworkCaptureModel.records)
        assertEquals(HomeworkCaptureModel.state(copy).groups, state.groups)
    }

    @Test fun later_and_done_expansion_preserves_ids_and_counts() {
        var state = HomeworkCaptureModel.state(copy)
        listOf(GroupStatus.Later, GroupStatus.Later, GroupStatus.Done).forEach {
            state = HomeworkCaptureModel.reduce(state, HomeworkEvent.ToggleGroup(it))
        }
        assertTrue(state.groups.none { it.collapsed })
        assertEquals((101L..107L).toList(), state.groups.flatMap { it.items }.map { it.id })
    }

    @Test fun obligation_set_is_finite_unique_and_editor_roots_keep_translated_origins() {
        assertEquals(32, HomeworkCaptureModel.obligations.size)
        assertEquals(32, HomeworkCaptureModel.obligations.distinct().size)
        listOf(24f, -24f).forEach { y ->
            val root = summaryRootWindowBounds(12f, y, 360f, 500f)
            assertEquals(SummaryBounds(12f, y, 372f, y + 500f), root)
            assertTrue(root.contains(SummaryBounds(20f, y + 20, 350f, y + 480)))
            assertFalse(root.contains(SummaryBounds(20f, y - 1, 350f, y + 480)))
        }
    }
}
