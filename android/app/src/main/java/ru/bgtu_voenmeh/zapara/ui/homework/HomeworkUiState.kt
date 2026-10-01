package ru.bgtu_voenmeh.zapara.ui.homework

import android.net.Uri
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.UiCopy
import ru.bgtu_voenmeh.zapara.data.Homework
import ru.bgtu_voenmeh.zapara.data.HomeworkStoredFile
import ru.bgtu_voenmeh.zapara.data.communities.HomeworkAudience
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

data class SharedHomeworkItemUi(
    val id: String,
    val communityId: String,
    val title: String,
    val body: String,
    val deadlineLabel: String,
    val completed: Boolean,
    val completionRevision: Long,
    val canComplete: Boolean,
    val audienceSelected: Boolean
)

data class SubjectPickerUi(val subjects: List<SubjectUi>, val query: String = "",
    val groupId: String = "", val profileName: String = "", val groupEpoch: Long = 0) {
    fun matches(group: String?, profile: String, epoch: Long): Boolean =
        groupId == group && profileName == profile && groupEpoch == epoch
}

data class HomeworkUiState(
    val loaded: Boolean = false,
    val hasGroup: Boolean = false,
    val groups: List<HomeworkGroupUi> = emptyList(),
    val editor: HomeworkEditorState? = null,
    val confirmDelete: Long? = null,
    val subjectPicker: SubjectPickerUi? = null,
    val guest: Boolean = false,
    val browseQuery: String = "",
    val browseFilter: HomeworkCompletionFilter = HomeworkCompletionFilter.Active,
    val deadlineFilter: HomeworkDeadlineFilter = HomeworkDeadlineFilter.All,
    val originFilter: HomeworkOriginFilter = HomeworkOriginFilter.All,
    val withFilesOnly: Boolean = false,
    val sortBySubject: Boolean = false,
    val undoDone: HomeworkUndoDone? = null,
    val undoDoneBusy: Boolean = false,
    val loadError: String? = null,
    val deleteBusy: Boolean = false,
    val deleteError: String? = null,
    val sharedRows: List<SharedHomeworkItemUi> = emptyList(),
    val sharedLoading: Boolean = false,
    val sharedError: String? = null,
    val sharedBusyIds: Set<String> = emptySet(),
    val personalBusyIds: Set<Long> = emptySet()
)

fun HomeworkUiState.resetBrowse(): HomeworkUiState =
    copy(browseQuery = "", browseFilter = HomeworkCompletionFilter.All,
        deadlineFilter = HomeworkDeadlineFilter.All, originFilter = HomeworkOriginFilter.All,
        withFilesOnly = false, sortBySubject = false)

fun HomeworkUiState.forGroupChange(): HomeworkUiState =
    copy(browseQuery = "", browseFilter = HomeworkCompletionFilter.Active,
        deadlineFilter = HomeworkDeadlineFilter.All, originFilter = HomeworkOriginFilter.All,
        withFilesOnly = false, sortBySubject = false, undoDone = null, undoDoneBusy = false)

internal enum class HomeworkEditDecision { Open, AlreadyOpen, ReplacePristine, Blocked }

internal fun homeworkEditDecision(editor: HomeworkEditorState?, targetId: Long): HomeworkEditDecision = when {
    editor == null -> HomeworkEditDecision.Open
    editor.id == targetId -> HomeworkEditDecision.AlreadyOpen
    editor.busy || editor.hasDraftChanges || editor.shareLoading || editor.shareRequest != null || editor.persistedId != null ->
        HomeworkEditDecision.Blocked
    else -> HomeworkEditDecision.ReplacePristine
}

internal fun homeworkEditStillAllowed(
    before: HomeworkEditorState?, current: HomeworkEditorState?, targetId: Long
): Boolean = before === current && homeworkEditDecision(current, targetId) in
    setOf(HomeworkEditDecision.Open, HomeworkEditDecision.ReplacePristine)

data class HomeworkUndoDone(val id: Long, val previousDone: Boolean, val groupId: String, val profileName: String) {
    fun canApply(profileName: String, groupId: String, currentDone: Boolean?): Boolean =
        this.profileName == profileName && this.groupId == groupId && currentDone == !previousDone
}

sealed interface HomeworkEvent {
    data class ToggleDone(val id: Long) : HomeworkEvent
    data class Edit(val id: Long) : HomeworkEvent
    data object Add : HomeworkEvent
    data object RetryLoad : HomeworkEvent
    data object RetryShared : HomeworkEvent
    data class ToggleShared(val id: String) : HomeworkEvent
    data class Query(val value: String) : HomeworkEvent
    data class BrowseQuery(val value: String) : HomeworkEvent
    data class BrowseFilter(val value: HomeworkCompletionFilter) : HomeworkEvent
    data class DeadlineFilter(val value: HomeworkDeadlineFilter) : HomeworkEvent
    data class OriginFilter(val value: HomeworkOriginFilter) : HomeworkEvent
    data class WithFilesOnly(val value: Boolean) : HomeworkEvent
    data class SortBySubject(val value: Boolean) : HomeworkEvent
    data object BrowseReset : HomeworkEvent
    data object UndoDone : HomeworkEvent
    data class PickSubject(val raw: String) : HomeworkEvent
    data class PickManualSubject(val raw: String) : HomeworkEvent
    data object ClosePicker : HomeworkEvent
    data class EditorText(val text: String) : HomeworkEvent
    data class EditorShare(val on: Boolean) : HomeworkEvent
    data class EditorAudience(val audience: HomeworkAudience) : HomeworkEvent
    data object RetryShareOptions : HomeworkEvent
    data object RetryShare : HomeworkEvent
    data object Inc : HomeworkEvent
    data object Dec : HomeworkEvent
    data object Recalculate : HomeworkEvent
    data object Save : HomeworkEvent
    data object Cancel : HomeworkEvent
    data class AskDelete(val id: Long) : HomeworkEvent
    data object ConfirmDelete : HomeworkEvent
    data object CancelDelete : HomeworkEvent
    data class ToggleGroup(val status: GroupStatus) : HomeworkEvent
    data object ExpandGroups : HomeworkEvent
    data object CollapseGroups : HomeworkEvent
    data class Attach(val kind: String, val uri: Uri) : HomeworkEvent
    data class RemoveFile(val id: String) : HomeworkEvent
    data class OpenFile(val homeworkId: Long, val fileId: String) : HomeworkEvent
}

enum class HomeworkEditorWork { Idle, Saving, Attachment, Recalculating }

data class HomeworkDraftSnapshot(val text: String, val n: Int, val share: Boolean, val files: Set<String>)

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
    val share: Boolean = false,
    val anchorDate: LocalDate? = null,
    val scheduleGroupId: String? = null,
    val sourceChanged: Boolean = false,
    val initial: HomeworkDraftSnapshot = HomeworkDraftSnapshot(text.trim(), n, share, files.map { it.id }.toSet()),
    val work: HomeworkEditorWork = HomeworkEditorWork.Idle,
    val error: String? = null,
    val persistedId: Long? = null,
    val shareAttempted: Boolean = false,
    val shareContext: HomeworkShareContext? = null,
    val shareLoading: Boolean = false,
    val audience: HomeworkAudience = HomeworkAudience(),
    val operationId: String = java.util.UUID.randomUUID().toString(),
    val shareRequest: HomeworkPublishSnapshot? = null
) {
    val busy: Boolean get() = work != HomeworkEditorWork.Idle
    val hasDraftChanges: Boolean get() = initial != HomeworkDraftSnapshot(text.trim(), n, share, files.map { it.id }.toSet())
    fun creationAnchor(clockDate: LocalDate): LocalDate = anchorDate ?: clockDate
    fun matchesSaveContext(groupId: String?, currentDue: LocalDate?): Boolean =
        (scheduleGroupId == null || scheduleGroupId == groupId) && dueFor(n,text) == currentDue
    val canSave: Boolean get() = HomeworkTextRules.valid(text) && !sourceChanged && !busy && shareRequest == null &&
        (!share || !shareLoading && shareContext != null && (!audience.selected || shareContext.supported && audience.valid()))
    fun hasChanges(existing: Homework): Boolean = text.trim() != existing.text || n != existing.n
    fun withText(value: String) = if (busy) this else copy(text = value, error = null)
    fun withShare(value: Boolean) = if (busy) this else copy(share = value, error = null)
    fun withAudience(value: HomeworkAudience) = if (busy) this else copy(audience = value, error = null)
    fun inc() = if (busy) this else copy(n = (n + 1).coerceAtMost(10), error = null)
    fun dec() = if (busy) this else copy(n = (n - 1).coerceAtLeast(1), error = null)
    fun dueText(copy: UiCopy): String {
        val due = dueFor(n, text) ?: return copy.get("hw_due_prefix", "—")
        return copy.get("hw_due_prefix", "${LessonFormat.dayMonth(due)} (${LessonFormat.weekdayShort(due, copy)})")
    }
}
