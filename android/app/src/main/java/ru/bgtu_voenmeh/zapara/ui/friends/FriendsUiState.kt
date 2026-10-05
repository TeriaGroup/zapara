package ru.bgtu_voenmeh.zapara.ui.friends

import ru.bgtu_voenmeh.zapara.data.GroupInfo

data class FriendUi(
    val id: Long,
    val index: Int,
    val groupName: String,
    val members: String,
    val enabled: Boolean,
    val colorIndex: Int
)

data class FriendEditorUi(
    val id: Long?,
    val index: Int?,
    val groupName: String,
    val members: String,
    val colorIndex: Int,
    val sourceGroupId: String = "",
    val sourceProfileName: String = "",
    val pickerOpen: Boolean = false,
    val manualOffline: Boolean = false
)

data class FriendsUiState(
    val loaded: Boolean = false,
    val failed: Boolean = false,
    val refreshing: Boolean = false,
    val refreshFailed: Boolean = false,
    val friends: List<FriendUi> = emptyList(),
    val canAdd: Boolean = true,
    val myGroupId: String = "",
    val profileName: String = "",
    val editor: FriendEditorUi? = null,
    val editorError: String? = null,
    val editorSaving: Boolean = false,
    val deletePending: Boolean = false,
    val deleteError: String? = null,
    val confirmDelete: Long? = null,
    val strictness: Int = 50,
    val alwaysShow: Boolean = false,
    val invert: Boolean = false,
    val groups: List<GroupInfo> = emptyList(),
    val previewLine: String = "",
    val encounters: List<FriendEncounter> = emptyList(),
    val missingGroups: List<String> = emptyList(),
    val checkedGroups: Int = 0,
    val hasOwnSchedule: Boolean = false,
    val meetingDate: java.time.LocalDate = java.time.LocalDate.now(),
    val meetingWindows: List<FriendMeetingWindows> = emptyList()
)

data class FriendMeetingWindows(val group: String, val windows: List<ru.bgtu_voenmeh.zapara.ui.FreeStudyInterval>)

internal fun FriendsUiState.deleteFailed(id: Long, error: String): FriendsUiState =
    if (confirmDelete != id) this else copy(deletePending = false, deleteError = error)

internal fun FriendsUiState.deleteAcknowledged(id: Long): FriendsUiState =
    if (confirmDelete != id) this else copy(confirmDelete = null,
        editor = editor?.takeUnless { it.id == id }, deletePending = false, deleteError = null)

sealed interface FriendsEvent {
    data class MeetingDate(val date: java.time.LocalDate) : FriendsEvent
    data object Retry : FriendsEvent
    data object RefreshSchedules : FriendsEvent
    data object Add : FriendsEvent
    data class Edit(val scope: FriendActionScope) : FriendsEvent
    data class EditorGroup(val name: String) : FriendsEvent
    data class ManualOffline(val enabled: Boolean) : FriendsEvent
    data class EditorMembers(val text: String) : FriendsEvent
    data class EditorColor(val index: Int) : FriendsEvent
    data object OpenPicker : FriendsEvent
    data object ClosePicker : FriendsEvent
    data object EditorSave : FriendsEvent
    data object EditorCancel : FriendsEvent
    data class Toggle(val scope: FriendActionScope, val enabled: Boolean) : FriendsEvent
    data class AskDelete(val id: Long) : FriendsEvent
    data object ConfirmDelete : FriendsEvent
    data object CancelDelete : FriendsEvent
    data class Strictness(val value: Int) : FriendsEvent
    data class AlwaysShow(val value: Boolean) : FriendsEvent
    data class Invert(val value: Boolean) : FriendsEvent
}
