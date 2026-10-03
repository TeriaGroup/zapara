package ru.bgtu_voenmeh.zapara.ui.schedule

import android.net.Uri
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkEditorState
import java.time.LocalDate

data class HomeworkRowUi(
    val id: Long,
    val text: String,
    val label: String,
    val status: String,
    val done: Boolean,
    val sharedId: String? = null,
    val canComplete: Boolean = true,
    val audienceLabel: String = ""
)

data class FriendDotUi(
    val index: Int,
    val groupName: String,
    val members: String,
    val score: Int,
    val hint: String,
    val intersectionScore: Int = score
)

data class SubgroupOptionUi(val id: String, val label: String)

data class SubgroupMarkUi(
    val streamId: String,
    val options: List<SubgroupOptionUi>,
    val chosenId: String?,
    val showChooser: Boolean
)

data class LessonUi(
    val index: Int,
    val timeStart: String,
    val timeEnd: String,
    val type: String,
    val name: String,
    val original: String?,
    val teacher: String,
    val room: String,
    val classroomRaw: String,
    val nextDate: String?,
    val homework: List<HomeworkRowUi>,
    val friends: List<FriendDotUi>,
    val isPast: Boolean,
    val subjectRaw: String,
    val subjectNorm: String,
    val remote: Boolean = false,
    val dayOfWeek: Int = 0,
    val subgroup: SubgroupMarkUi? = null,
    val typeRaw: String = ""
) {
    val hasMapLocation: Boolean
        get() {
            if (remote) return false
            val location = classroomRaw.trim().trimEnd(';').trim()
            return location.isNotBlank() && location != "-" && location != "—" &&
                location != "–" && location != "?"
        }
}

data class DayPage(
    val date: LocalDate,
    val isToday: Boolean,
    val caption: String,
    val lessons: List<LessonUi>,
    val nextHint: String?,
    val isSunday: Boolean,
    val deadlines: List<HomeworkRowUi> = emptyList(),
    val dataState: String? = null,
    val nextKnownDate: LocalDate? = null,
    val transfers: List<LessonTransfer> = emptyList()
)

data class LessonTransfer(val from: String, val to: String, val destinationRaw: String,
    val assessment: ru.bgtu_voenmeh.zapara.ui.TransferAssessment)

data class RenameUi(
    val lesson: LessonUi,
    val name: String,
    val note: String,
    val scope: Int,
    val hasExisting: Boolean,
    val original: String,
    val dayName: String,
    val existingScopes: Set<Int> = emptySet(),
    val profileName: String = "",
    val groupId: String = "",
    val selectedDate: LocalDate? = null,
    val affectedGlobal: List<String> = emptyList(),
    val affectedWeekday: List<String> = emptyList(),
    val busy: Boolean = false,
    val error: String? = null
)

enum class SubjectRowsStatus { Loading, Ready, Failed }

data class SubjectHomeworkRequest(val ticket: Long, val profileName: String, val groupId: String, val subjectNorm: String) {
    fun matches(ticket: Long, profileName: String, groupId: String, lesson: LessonUi?): Boolean =
        this.ticket == ticket && this.profileName == profileName && this.groupId == groupId &&
            lesson?.subjectNorm == subjectNorm
}

data class ScheduleCompletionUndo(val id: Long, val previousDone: Boolean,
    val profileName: String, val groupId: String) {
    fun canApply(profile: String, group: String, currentDone: Boolean?): Boolean =
        profileName == profile && groupId == group && currentDone == !previousDone
}

data class ScheduleUiState(
    val loaded: Boolean = false,
    val hasGroup: Boolean = false,
    val today: LocalDate = LocalDate.now(),
    val selected: LocalDate = today,
    val pages: Map<LocalDate, DayPage> = emptyMap(),
    val refreshing: Boolean = false,
    val actionsFor: LessonUi? = null,
    val rename: RenameUi? = null,
    val homeworkEditor: HomeworkEditorState? = null,
    val error: String? = null,
    val guest: Boolean = false,
    val undoDone: ScheduleCompletionUndo? = null,
    val completionBusyIds: Set<Long> = emptySet(),
    val undoDoneBusy: Boolean = false,
    val sharedBusyIds: Set<String> = emptySet(),
    val now: java.time.LocalDateTime = java.time.LocalDateTime.now(),
    val sourceStatus: String? = null,
    val undoShared: Pair<String, Boolean>? = null,
    val subjectHomework: LessonUi? = null,
    val subjectRows: List<HomeworkRowUi> = emptyList(),
    val subjectRowsStatus: SubjectRowsStatus = SubjectRowsStatus.Ready,
    val sharedDetail: HomeworkRowUi? = null,
    val groupId: String = "",
    val profileName: String = "",
    val undoSubgroup: SubgroupUndoUi? = null
)

sealed interface ScheduleEvent {
    data class Need(val date: LocalDate) : ScheduleEvent
    data class Select(val date: LocalDate) : ScheduleEvent
    data object UndoShared : ScheduleEvent
    data object UndoDone : ScheduleEvent
    data class QuickDay(val offset: Int) : ScheduleEvent
    data object Today : ScheduleEvent
    data object SyncClock : ScheduleEvent
    data object Retry : ScheduleEvent
    data object RefreshShared : ScheduleEvent
    data object Refresh : ScheduleEvent
    data class LongPress(val lesson: LessonUi) : ScheduleEvent
    data object CloseActions : ScheduleEvent
    data class Rename(val lesson: LessonUi) : ScheduleEvent
    data class RenameChanged(val name: String, val note: String, val scope: Int) : ScheduleEvent
    data class RenameSave(val draft: RenameUi) : ScheduleEvent
    data class RenameReset(val draft: RenameUi) : ScheduleEvent
    data object RenameCancel : ScheduleEvent
    data class SubjectHomework(val lesson: LessonUi) : ScheduleEvent
    data object CloseSubjectHomework : ScheduleEvent
    data class OpenHomework(val row: HomeworkRowUi) : ScheduleEvent
    data class ToggleShared(val id: String, val done: Boolean,
        val groupId: String? = null, val profileName: String? = null) : ScheduleEvent
    data class ToggleDone(val id: Long, val expectedDone: Boolean? = null,
        val groupId: String? = null, val profileName: String? = null) : ScheduleEvent
    data class AddHomework(val lesson: LessonUi) : ScheduleEvent
    data class HomeworkEditorText(val text: String) : ScheduleEvent
    data class HomeworkEditorShare(val on: Boolean) : ScheduleEvent
    data class HomeworkEditorAudience(val audience: ru.bgtu_voenmeh.zapara.data.communities.HomeworkAudience) : ScheduleEvent
    data object HomeworkRetryShareOptions : ScheduleEvent
    data object HomeworkRetryShare : ScheduleEvent
    data object HomeworkEditorInc : ScheduleEvent
    data object HomeworkEditorDec : ScheduleEvent
    data object RecalculateHomework : ScheduleEvent
    data object HomeworkEditorSave : ScheduleEvent
    data object HomeworkApproveDuplicate : ScheduleEvent
    data object HomeworkCancelDuplicate : ScheduleEvent
    data object HomeworkEditorCancel : ScheduleEvent
    data class HomeworkAttach(val kind: String, val uri: Uri) : ScheduleEvent
    data class HomeworkAttachMany(val kind: String, val uris: List<Uri>) : ScheduleEvent
    data class HomeworkRemoveFile(val id: String) : ScheduleEvent
    data class OpenMap(val lesson: LessonUi) : ScheduleEvent
    data class PickSubgroup(val streamId: String, val optionId: String,
        val groupId: String? = null, val profileName: String? = null) : ScheduleEvent
    data object UndoSubgroup : ScheduleEvent
}
