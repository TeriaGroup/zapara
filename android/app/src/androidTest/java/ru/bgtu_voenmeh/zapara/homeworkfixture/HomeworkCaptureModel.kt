package ru.bgtu_voenmeh.zapara

import ru.bgtu_voenmeh.zapara.data.Homework
import ru.bgtu_voenmeh.zapara.ui.UiCopy
import ru.bgtu_voenmeh.zapara.ui.homework.*
import java.time.LocalDate

/** Fixed input, not a homework service. No storage, status recomputation or Save algorithm. */
internal object HomeworkCaptureModel {
    val today: LocalDate = LocalDate.of(2026, 9, 12)
    private val created = LocalDate.of(2026, 9, 1)
    val records = listOf(
        record(101, "overdue", 11), record(102, "burning_urgent", 12),
        record(103, "burning", 13), record(104, "approaching", 14),
        record(105, "far", 20), record(106, "pending", null), record(107, "done", 10)
    )
    val expectedDue = listOf("срок 11.09 (Пт)", "срок 12.09 (Сб)", "срок 13.09 (Вс)",
        "срок 14.09 (Пн)", "срок 20.09 (Вс)", "срок —", "срок 10.09 (Чт)")
    val expectedStatus = listOf("Просрочено", "Горит сегодня", "Горит", "Скоро", "Позже", "Позже", "Сдано")
    val obligations = listOf("top") + GroupStatus.entries.map { "group-$it" } +
        records.map { "row-${it.id}" } + listOf("bottom", "toggle-done", "toggle-restored") +
        listOf(101L, 106L).flatMap { id -> listOf("open", "text", "n", "n-return", "revert", "cancel", "reopen", "save-callback")
            .map { "editor-$id-$it" } }

    private fun record(id: Long, status: String, day: Int?) = Homework(id, "математика", "Конспект $id",
        created, 1, day?.let { LocalDate.of(2026, 9, it) }, status, status == "done")

    fun state(copy: UiCopy) = HomeworkUiState(true, true, HomeworkGroups.group(records.map {
        HomeworkGroups.toItem(it, "Математика", today, copy, it.norm)
    }, copy))

    fun editor(id: Long): HomeworkEditorState {
        val original = records.single { it.id == id }
        return HomeworkEditorState(id, original.norm, "Математика", original.text, original.n, true,
            homeworkEditorDueFor(original, today) { from, n -> from.plusDays(n.toLong()) })
    }

    fun reduce(state: HomeworkUiState, event: HomeworkEvent): HomeworkUiState = when (event) {
        is HomeworkEvent.ToggleGroup -> state.copy(groups = state.groups.map {
            if (it.status == event.status) it.copy(collapsed = !it.collapsed) else it
        })
        is HomeworkEvent.Edit -> state.copy(editor = editor(event.id))
        is HomeworkEvent.EditorText -> state.copy(editor = requireNotNull(state.editor).withText(event.text))
        HomeworkEvent.Inc -> state.copy(editor = requireNotNull(state.editor).inc())
        HomeworkEvent.Dec -> state.copy(editor = requireNotNull(state.editor).dec())
        HomeworkEvent.Cancel -> state.copy(editor = null)
        // Deliberately only a UI callback; immutable records are never updated.
        HomeworkEvent.Save -> { check(requireNotNull(state.editor).canSave); state.copy(editor = null) }
        else -> error("Unsupported homework fixture event: $event")
    }
}
