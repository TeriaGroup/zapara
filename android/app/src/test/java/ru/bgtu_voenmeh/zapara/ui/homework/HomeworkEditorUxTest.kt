package ru.bgtu_voenmeh.zapara.ui.homework

import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.HomeworkStoredFile

class HomeworkEditorUxTest {
    private fun editor() = HomeworkEditorState(null, "Матан", "Матан", "Задача 5", 1, false, { _, _ -> null }, draft = "draft")
    private val file = HomeworkStoredFile("f1", "document", "Задание.pdf", "application/pdf", staged = true)

    @Test fun changes_and_reverted_fields_are_compared_to_the_opening_snapshot() {
        val original = editor()
        assertFalse(original.hasDraftChanges)
        assertTrue(original.withText("Задача 6").hasDraftChanges)
        assertFalse(original.withText("Задача 6").withText("Задача 5").hasDraftChanges)
        assertTrue(original.inc().hasDraftChanges)
        assertFalse(original.inc().dec().hasDraftChanges)
        assertTrue(original.withShare(true).hasDraftChanges)
        assertFalse(original.withShare(true).withShare(false).hasDraftChanges)
        val attached = original.copy(files = listOf(file))
        assertTrue(attached.hasDraftChanges)
        assertFalse(attached.copy(files = emptyList(), removed = setOf(file.id)).hasDraftChanges)
    }

    @Test fun busy_work_prevents_a_second_save_and_field_changes() {
        for (work in HomeworkEditorWork.entries.filter { it != HomeworkEditorWork.Idle }) {
            val busy = editor().copy(work = work)
            assertFalse(busy.canSave)
            assertEquals(busy, busy.withText("new"))
            assertEquals(busy, busy.withShare(true))
            assertEquals(busy, busy.inc())
            assertEquals(busy, busy.dec())
        }
    }

    @Test fun a_file_failure_keeps_the_written_row_for_retry_without_a_duplicate() {
        var current = editor().copy(files = listOf(file))
        var creates = 0
        val writes = mutableListOf<Long?>()
        val save: (Long?) -> Long = { id -> writes += id; id ?: (++creates).toLong() }
        assertThrows(IllegalStateException::class.java) {
            persistHomeworkEditor(current, { current = it }, save) { throw IllegalStateException("disk full") }
        }
        assertEquals(1L, current.persistedId)
        assertEquals(listOf(file), current.files)
        current = persistHomeworkEditor(current, { current = it }, save) { listOf(file.copy(staged = false)) }
        assertEquals(1, creates)
        assertEquals(listOf(null, 1L), writes)
        assertEquals("Задача 5", current.text)
        assertFalse(current.files.single().staged)
    }

    @Test fun failed_local_write_does_not_mark_progress_or_commit_files() {
        val original = editor()
        var current = original
        var filesTouched = false
        assertThrows(IllegalStateException::class.java) {
            persistHomeworkEditor(original, { current = it }, { throw IllegalStateException("database unavailable") }) {
                filesTouched = true
                emptyList()
            }
        }
        assertEquals(original, current)
        assertFalse(filesTouched)
    }

    @Test fun editing_an_existing_task_reuses_its_id() {
        var current = editor().copy(id = 42L, isEdit = true)
        val writes = mutableListOf<Long?>()
        current = persistHomeworkEditor(current, { current = it }, { id -> writes += id; requireNotNull(id) }) { emptyList() }
        assertEquals(listOf(42L), writes)
        assertEquals(42L, current.persistedId)
    }

    @Test fun file_staging_metadata_is_not_a_user_edit() {
        val original = HomeworkEditorState(42, "Матан", "Матан", "Задача 5", 1, true, { _, _ -> null }, files = listOf(file))
        assertFalse(original.copy(files = listOf(file.copy(staged = false)), error = "failure").hasDraftChanges)
        assertTrue(original.copy(files = emptyList()).hasDraftChanges)
    }
}
