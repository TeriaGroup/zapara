package ru.bgtu_voenmeh.zapara.ui.homework

import android.net.Uri
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.UiCopy
import ru.bgtu_voenmeh.zapara.data.Homework
import ru.bgtu_voenmeh.zapara.data.HomeworkStoredFile
import java.time.LocalDate

enum class GroupStatus { Overdue, Burning, Soon, Later, Done }

internal fun homeworkEditorDueFor(
    existing: Homework?, today: LocalDate, compute: (LocalDate, Int) -> LocalDate?
): (Int, String) -> LocalDate? = { n, text ->
    if (existing != null && n == existing.n && text.trim() == existing.text) existing.due
    else compute(existing?.createdAt ?: today, n)
}

data class HomeworkItemUi(
    val id: Long,
    val subject: String,
    val text: String,
    val dueLabel: String,
    val status: String,
    val done: Boolean,
    val subjectRaw: String = "",
    val n: Int = 1,
    val due: LocalDate? = null,
    val statusLabel: String = "",
    val files: List<HomeworkStoredFile> = emptyList()
)

data class HomeworkGroupUi(
    val status: GroupStatus,
    val title: String,
    val items: List<HomeworkItemUi>,
    val collapsed: Boolean
)

data class SubjectUi(val raw: String, val norm: String, val display: String, val type: String = "")

data class SubjectPickerUi(val subjects: List<SubjectUi>, val query: String = "")

data class HomeworkUiState(
    val loaded: Boolean = false,
    val hasGroup: Boolean = false,
    val groups: List<HomeworkGroupUi> = emptyList(),
    val editor: HomeworkEditorState? = null,
    val confirmDelete: Long? = null,
    val subjectPicker: SubjectPickerUi? = null,
    val guest: Boolean = false
)

sealed interface HomeworkEvent {
    data class ToggleDone(val id: Long) : HomeworkEvent
    data class Edit(val id: Long) : HomeworkEvent
    data object Add : HomeworkEvent
    data class Query(val value: String) : HomeworkEvent
    data class PickSubject(val raw: String) : HomeworkEvent
    data object ClosePicker : HomeworkEvent
    data class EditorText(val text: String) : HomeworkEvent
    data class EditorShare(val on: Boolean) : HomeworkEvent
    data object Inc : HomeworkEvent
    data object Dec : HomeworkEvent
    data object Save : HomeworkEvent
    data object Cancel : HomeworkEvent
    data class AskDelete(val id: Long) : HomeworkEvent
    data object ConfirmDelete : HomeworkEvent
    data object CancelDelete : HomeworkEvent
    data class ToggleGroup(val status: GroupStatus) : HomeworkEvent
    data class Attach(val kind: String, val uri: Uri) : HomeworkEvent
    data class RemoveFile(val id: String) : HomeworkEvent
    data class OpenFile(val homeworkId: Long, val fileId: String) : HomeworkEvent
}

data class HomeworkEditorState(
    val id: Long?,
    val subjectRaw: String,
    val subjectDisplay: String,
    val text: String,
    val n: Int,
    val isEdit: Boolean,
    val dueFor: (Int, String) -> LocalDate?,
    val files: List<HomeworkStoredFile> = emptyList(),
    val draft: String = "",
    val removed: Set<String> = emptySet(),
    val share: Boolean = false
) {
    val canSave: Boolean get() = text.trim().isNotEmpty()
    fun hasChanges(existing: Homework): Boolean = text.trim() != existing.text || n != existing.n
    fun withText(value: String) = copy(text = value)
    fun inc() = copy(n = (n + 1).coerceAtMost(10))
    fun dec() = copy(n = (n - 1).coerceAtLeast(1))
    fun dueText(copy: UiCopy): String {
        val due = dueFor(n, text) ?: return copy.get("hw_due_prefix", "—")
        return copy.get("hw_due_prefix", "${LessonFormat.dayMonth(due)} (${LessonFormat.weekdayShort(due, copy)})")
    }
}
