package ru.bgtu_voenmeh.zapara.ui.schedule

import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkEditorState
import java.time.LocalDate

data class HomeworkRowUi(
    val id: Long,
    val text: String,
    val label: String,
    val status: String,
    val done: Boolean
)

data class FriendDotUi(
    val index: Int,
    val groupName: String,
    val members: String,
    val score: Int,
    val hint: String
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
    val dayOfWeek: Int = 0
)

data class DayPage(
    val date: LocalDate,
    val isToday: Boolean,
    val caption: String,
    val lessons: List<LessonUi>,
    val nextHint: String?,
    val isSunday: Boolean
)

data class RenameUi(
    val lesson: LessonUi,
    val name: String,
    val note: String,
    val scope: Int,
    val hasExisting: Boolean,
    val original: String,
    val dayName: String
)

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
    val error: String? = null
)

sealed interface ScheduleEvent {
    data class Need(val date: LocalDate) : ScheduleEvent
    data class Select(val date: LocalDate) : ScheduleEvent
    data object Today : ScheduleEvent
    data object Retry : ScheduleEvent
    data object Refresh : ScheduleEvent
    data class LongPress(val lesson: LessonUi) : ScheduleEvent
    data object CloseActions : ScheduleEvent
    data class Rename(val lesson: LessonUi) : ScheduleEvent
    data class RenameChanged(val name: String, val note: String, val scope: Int) : ScheduleEvent
    data object RenameSave : ScheduleEvent
    data object RenameReset : ScheduleEvent
    data object RenameCancel : ScheduleEvent
    data class ToggleDone(val id: Long) : ScheduleEvent
    data class AddHomework(val lesson: LessonUi) : ScheduleEvent
    data class HomeworkEditorText(val text: String) : ScheduleEvent
    data object HomeworkEditorInc : ScheduleEvent
    data object HomeworkEditorDec : ScheduleEvent
    data object HomeworkEditorSave : ScheduleEvent
    data object HomeworkEditorCancel : ScheduleEvent
    data class OpenMap(val lesson: LessonUi) : ScheduleEvent
}
